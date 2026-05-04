#include "InjectorArguments.h"

#include <Windows.h>

#include <iostream>
#include <string>

namespace
{
    constexpr DWORD InjectionProcessAccess =
        PROCESS_CREATE_THREAD
        | PROCESS_QUERY_INFORMATION
        | PROCESS_VM_OPERATION
        | PROCESS_VM_WRITE
        | PROCESS_VM_READ;
    constexpr wchar_t StartMonitorExportName[] = L"StartMonitor";

    void PrintLastError(const wchar_t* apiName)
    {
        std::wcerr << apiName << L" failed with error " << GetLastError() << L'\n';
    }

    void* ResolveRemoteExportAddress(
        const std::wstring& dllPath,
        DWORD_PTR remoteModuleBase,
        const char* exportName)
    {
        HMODULE localModule = LoadLibraryExW(dllPath.c_str(), nullptr, DONT_RESOLVE_DLL_REFERENCES);
        if (localModule == nullptr)
        {
            PrintLastError(L"LoadLibraryExW");
            return nullptr;
        }

        FARPROC localExport = GetProcAddress(localModule, exportName);
        if (localExport == nullptr)
        {
            std::wcerr << StartMonitorExportName << L" export not found\n";
            FreeLibrary(localModule);
            return nullptr;
        }

        const auto exportOffset = reinterpret_cast<DWORD_PTR>(localExport)
            - reinterpret_cast<DWORD_PTR>(localModule);
        FreeLibrary(localModule);
        return reinterpret_cast<void*>(remoteModuleBase + exportOffset);
    }

    bool StartRemoteMonitor(HANDLE processHandle, const std::wstring& dllPath, DWORD_PTR remoteModuleBase)
    {
        void* startMonitorAddress = ResolveRemoteExportAddress(
            dllPath,
            remoteModuleBase,
            "StartMonitor");
        if (startMonitorAddress == nullptr)
        {
            return false;
        }

        HANDLE threadHandle = CreateRemoteThread(
            processHandle,
            nullptr,
            0,
            reinterpret_cast<LPTHREAD_START_ROUTINE>(startMonitorAddress),
            nullptr,
            0,
            nullptr);
        if (threadHandle == nullptr)
        {
            PrintLastError(L"CreateRemoteThread");
            return false;
        }

        const DWORD waitResult = WaitForSingleObject(threadHandle, 15000);
        DWORD exitCode = 0;
        const bool succeeded = waitResult == WAIT_OBJECT_0
            && GetExitCodeThread(threadHandle, &exitCode)
            && exitCode != 0;
        if (!succeeded)
        {
            std::wcerr << L"StartMonitor failed with result " << waitResult
                << L" and exit code " << exitCode << L'\n';
        }

        CloseHandle(threadHandle);
        return succeeded;
    }

    bool InjectLibrary(DWORD processId, const std::wstring& dllPath)
    {
        const std::size_t byteLength = (dllPath.size() + 1) * sizeof(wchar_t);
        HANDLE processHandle = OpenProcess(InjectionProcessAccess, FALSE, processId);
        if (processHandle == nullptr)
        {
            PrintLastError(L"OpenProcess");
            return false;
        }

        void* remotePath = nullptr;
        HANDLE threadHandle = nullptr;
        SIZE_T bytesWritten = 0;
        HMODULE kernel32Handle = nullptr;
        LPTHREAD_START_ROUTINE loadLibraryAddress = nullptr;
        DWORD waitResult = 0;
        DWORD exitCode = 0;
        bool succeeded = false;
        remotePath = VirtualAllocEx(
            processHandle,
            nullptr,
            byteLength,
            MEM_COMMIT | MEM_RESERVE,
            PAGE_READWRITE);
        if (remotePath == nullptr)
        {
            PrintLastError(L"VirtualAllocEx");
            goto Cleanup;
        }

        if (!WriteProcessMemory(
            processHandle,
            remotePath,
            dllPath.c_str(),
            byteLength,
            &bytesWritten)
            || bytesWritten != byteLength)
        {
            PrintLastError(L"WriteProcessMemory");
            goto Cleanup;
        }

        kernel32Handle = GetModuleHandleW(L"kernel32.dll");
        if (kernel32Handle == nullptr)
        {
            PrintLastError(L"GetModuleHandleW");
            goto Cleanup;
        }

        loadLibraryAddress = reinterpret_cast<LPTHREAD_START_ROUTINE>(
            GetProcAddress(kernel32Handle, "LoadLibraryW"));
        if (loadLibraryAddress == nullptr)
        {
            PrintLastError(L"GetProcAddress");
            goto Cleanup;
        }

        threadHandle = CreateRemoteThread(
            processHandle,
            nullptr,
            0,
            loadLibraryAddress,
            remotePath,
            0,
            nullptr);
        if (threadHandle == nullptr)
        {
            PrintLastError(L"CreateRemoteThread");
            goto Cleanup;
        }

        waitResult = WaitForSingleObject(threadHandle, 15000);
        if (waitResult != WAIT_OBJECT_0)
        {
            std::wcerr << L"Remote loader wait failed with result " << waitResult << L'\n';
            // The remote loader thread may still be reading this buffer after our wait expires.
            // Once the thread has started, only free the path after definitive completion.
            remotePath = nullptr;
            goto Cleanup;
        }

        if (!GetExitCodeThread(threadHandle, &exitCode) || exitCode == 0)
        {
            PrintLastError(L"GetExitCodeThread");
            goto Cleanup;
        }

        std::wcout << L"LoadLibrary exit=0x" << std::hex << exitCode << L'\n';
        succeeded = StartRemoteMonitor(processHandle, dllPath, static_cast<DWORD_PTR>(exitCode));

Cleanup:
        if (threadHandle != nullptr)
        {
            CloseHandle(threadHandle);
        }

        if (remotePath != nullptr)
        {
            VirtualFreeEx(processHandle, remotePath, 0, MEM_RELEASE);
        }

        CloseHandle(processHandle);
        return succeeded;
    }
}

int wmain(int argc, wchar_t* argv[])
{
    BO2InjectorHelper::InjectorArguments arguments{};
    const BO2InjectorHelper::ParseArgumentsStatus parseStatus =
        BO2InjectorHelper::ParseInjectorArguments(argc, argv, arguments);
    if (parseStatus == BO2InjectorHelper::ParseArgumentsStatus::Usage)
    {
        std::wcerr << L"Usage: BO2InjectorHelper.exe <process-id> <dll-path>\n";
        return 2;
    }

    if (parseStatus == BO2InjectorHelper::ParseArgumentsStatus::InvalidProcessId)
    {
        std::wcerr << L"Invalid process id\n";
        return 2;
    }

    const std::wstring& dllPath = arguments.DllPath;
    if (GetFileAttributesW(dllPath.c_str()) == INVALID_FILE_ATTRIBUTES)
    {
        PrintLastError(L"GetFileAttributesW");
        return 2;
    }

    return InjectLibrary(arguments.ProcessId, dllPath) ? 0 : 1;
}
