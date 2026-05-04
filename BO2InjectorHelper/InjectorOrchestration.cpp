#include "InjectorOrchestration.h"

#include <iostream>

namespace BO2InjectorHelper
{
    namespace
    {
        constexpr wchar_t StartMonitorExportName[] = L"StartMonitor";

        void PrintLastError(IInjectorWindowsApi& api, const wchar_t* apiName)
        {
            std::wcerr << apiName << L" failed with error " << api.GetLastError() << L'\n';
        }
    }

    HANDLE Win32InjectorWindowsApi::OpenProcess(DWORD desiredAccess, BOOL inheritHandle, DWORD processId)
    {
        return ::OpenProcess(desiredAccess, inheritHandle, processId);
    }

    LPVOID Win32InjectorWindowsApi::VirtualAllocEx(
        HANDLE processHandle,
        LPVOID address,
        SIZE_T size,
        DWORD allocationType,
        DWORD protect)
    {
        return ::VirtualAllocEx(processHandle, address, size, allocationType, protect);
    }

    BOOL Win32InjectorWindowsApi::WriteProcessMemory(
        HANDLE processHandle,
        LPVOID baseAddress,
        LPCVOID buffer,
        SIZE_T size,
        SIZE_T* bytesWritten)
    {
        return ::WriteProcessMemory(processHandle, baseAddress, buffer, size, bytesWritten);
    }

    HMODULE Win32InjectorWindowsApi::GetModuleHandleW(LPCWSTR moduleName)
    {
        return ::GetModuleHandleW(moduleName);
    }

    FARPROC Win32InjectorWindowsApi::GetProcAddress(HMODULE moduleHandle, LPCSTR procedureName)
    {
        return ::GetProcAddress(moduleHandle, procedureName);
    }

    HANDLE Win32InjectorWindowsApi::CreateRemoteThread(
        HANDLE processHandle,
        LPSECURITY_ATTRIBUTES threadAttributes,
        SIZE_T stackSize,
        LPTHREAD_START_ROUTINE startAddress,
        LPVOID parameter,
        DWORD creationFlags,
        LPDWORD threadId)
    {
        return ::CreateRemoteThread(
            processHandle,
            threadAttributes,
            stackSize,
            startAddress,
            parameter,
            creationFlags,
            threadId);
    }

    DWORD Win32InjectorWindowsApi::WaitForSingleObject(HANDLE handle, DWORD milliseconds)
    {
        return ::WaitForSingleObject(handle, milliseconds);
    }

    BOOL Win32InjectorWindowsApi::GetExitCodeThread(HANDLE threadHandle, LPDWORD exitCode)
    {
        return ::GetExitCodeThread(threadHandle, exitCode);
    }

    BOOL Win32InjectorWindowsApi::VirtualFreeEx(HANDLE processHandle, LPVOID address, SIZE_T size, DWORD freeType)
    {
        return ::VirtualFreeEx(processHandle, address, size, freeType);
    }

    BOOL Win32InjectorWindowsApi::CloseHandle(HANDLE handle)
    {
        return ::CloseHandle(handle);
    }

    HMODULE Win32InjectorWindowsApi::LoadLibraryExW(LPCWSTR fileName, HANDLE fileHandle, DWORD flags)
    {
        return ::LoadLibraryExW(fileName, fileHandle, flags);
    }

    BOOL Win32InjectorWindowsApi::FreeLibrary(HMODULE moduleHandle)
    {
        return ::FreeLibrary(moduleHandle);
    }

    DWORD Win32InjectorWindowsApi::GetLastError()
    {
        return ::GetLastError();
    }

    void* CalculateRemoteExportAddress(
        DWORD_PTR localModuleBase,
        DWORD_PTR localExportAddress,
        DWORD_PTR remoteModuleBase) noexcept
    {
        if (localModuleBase == 0 || localExportAddress < localModuleBase || remoteModuleBase == 0)
        {
            return nullptr;
        }

        const DWORD_PTR exportOffset = localExportAddress - localModuleBase;
        return reinterpret_cast<void*>(remoteModuleBase + exportOffset);
    }

    bool ResolveRemoteExportAddress(
        IInjectorWindowsApi& api,
        const std::wstring& dllPath,
        DWORD_PTR remoteModuleBase,
        const char* exportName,
        void*& remoteExportAddress)
    {
        remoteExportAddress = nullptr;
        HMODULE localModule = api.LoadLibraryExW(dllPath.c_str(), nullptr, DONT_RESOLVE_DLL_REFERENCES);
        if (localModule == nullptr)
        {
            PrintLastError(api, L"LoadLibraryExW");
            return false;
        }

        FARPROC localExport = api.GetProcAddress(localModule, exportName);
        if (localExport == nullptr)
        {
            std::wcerr << StartMonitorExportName << L" export not found\n";
            api.FreeLibrary(localModule);
            return false;
        }

        remoteExportAddress = CalculateRemoteExportAddress(
            reinterpret_cast<DWORD_PTR>(localModule),
            reinterpret_cast<DWORD_PTR>(localExport),
            remoteModuleBase);
        api.FreeLibrary(localModule);
        return remoteExportAddress != nullptr;
    }

    bool StartRemoteMonitor(
        IInjectorWindowsApi& api,
        HANDLE processHandle,
        const std::wstring& dllPath,
        DWORD_PTR remoteModuleBase,
        const InjectorOrchestrationOptions& options)
    {
        void* startMonitorAddress = nullptr;
        if (!ResolveRemoteExportAddress(api, dllPath, remoteModuleBase, "StartMonitor", startMonitorAddress))
        {
            return false;
        }

        HANDLE threadHandle = api.CreateRemoteThread(
            processHandle,
            nullptr,
            0,
            reinterpret_cast<LPTHREAD_START_ROUTINE>(startMonitorAddress),
            nullptr,
            0,
            nullptr);
        if (threadHandle == nullptr)
        {
            PrintLastError(api, L"CreateRemoteThread");
            return false;
        }

        const DWORD waitResult = api.WaitForSingleObject(threadHandle, options.WaitMilliseconds);
        DWORD exitCode = 0;
        const bool succeeded = waitResult == WAIT_OBJECT_0
            && api.GetExitCodeThread(threadHandle, &exitCode)
            && exitCode != 0;
        if (!succeeded)
        {
            std::wcerr << L"StartMonitor failed with result " << waitResult
                << L" and exit code " << exitCode << L'\n';
        }

        api.CloseHandle(threadHandle);
        return succeeded;
    }

    bool InjectLibrary(
        IInjectorWindowsApi& api,
        DWORD processId,
        const std::wstring& dllPath,
        const InjectorOrchestrationOptions& options)
    {
        const std::size_t byteLength = (dllPath.size() + 1) * sizeof(wchar_t);
        HANDLE processHandle = api.OpenProcess(InjectionProcessAccess, FALSE, processId);
        if (processHandle == nullptr)
        {
            PrintLastError(api, L"OpenProcess");
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

        remotePath = api.VirtualAllocEx(
            processHandle,
            nullptr,
            byteLength,
            MEM_COMMIT | MEM_RESERVE,
            PAGE_READWRITE);
        if (remotePath == nullptr)
        {
            PrintLastError(api, L"VirtualAllocEx");
            goto Cleanup;
        }

        if (!api.WriteProcessMemory(
            processHandle,
            remotePath,
            dllPath.c_str(),
            byteLength,
            &bytesWritten)
            || bytesWritten != byteLength)
        {
            PrintLastError(api, L"WriteProcessMemory");
            goto Cleanup;
        }

        kernel32Handle = api.GetModuleHandleW(L"kernel32.dll");
        if (kernel32Handle == nullptr)
        {
            PrintLastError(api, L"GetModuleHandleW");
            goto Cleanup;
        }

        loadLibraryAddress = reinterpret_cast<LPTHREAD_START_ROUTINE>(
            api.GetProcAddress(kernel32Handle, "LoadLibraryW"));
        if (loadLibraryAddress == nullptr)
        {
            PrintLastError(api, L"GetProcAddress");
            goto Cleanup;
        }

        threadHandle = api.CreateRemoteThread(
            processHandle,
            nullptr,
            0,
            loadLibraryAddress,
            remotePath,
            0,
            nullptr);
        if (threadHandle == nullptr)
        {
            PrintLastError(api, L"CreateRemoteThread");
            goto Cleanup;
        }

        waitResult = api.WaitForSingleObject(threadHandle, options.WaitMilliseconds);
        if (waitResult != WAIT_OBJECT_0)
        {
            std::wcerr << L"Remote loader wait failed with result " << waitResult << L'\n';
            // The remote loader thread may still be reading this buffer after our wait expires.
            // Once the thread has started, only free the path after definitive completion.
            remotePath = nullptr;
            goto Cleanup;
        }

        if (!api.GetExitCodeThread(threadHandle, &exitCode) || exitCode == 0)
        {
            PrintLastError(api, L"GetExitCodeThread");
            goto Cleanup;
        }

        std::wcout << L"LoadLibrary exit=0x" << std::hex << exitCode << L'\n';
        succeeded = StartRemoteMonitor(api, processHandle, dllPath, static_cast<DWORD_PTR>(exitCode), options);

Cleanup:
        if (threadHandle != nullptr)
        {
            api.CloseHandle(threadHandle);
        }

        if (remotePath != nullptr)
        {
            api.VirtualFreeEx(processHandle, remotePath, 0, MEM_RELEASE);
        }

        api.CloseHandle(processHandle);
        return succeeded;
    }
}
