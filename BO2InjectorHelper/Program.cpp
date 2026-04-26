#include <Windows.h>

#include <cwchar>
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

    void PrintLastError(const wchar_t* apiName)
    {
        std::wcerr << apiName << L" failed with error " << GetLastError() << L'\n';
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
            goto Cleanup;
        }

        if (!GetExitCodeThread(threadHandle, &exitCode) || exitCode == 0)
        {
            PrintLastError(L"GetExitCodeThread");
            goto Cleanup;
        }

        std::wcout << L"LoadLibrary exit=0x" << std::hex << exitCode << L'\n';
        succeeded = true;

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
    if (argc != 3)
    {
        std::wcerr << L"Usage: BO2InjectorHelper.exe <process-id> <dll-path>\n";
        return 2;
    }

    wchar_t* end = nullptr;
    const unsigned long processIdValue = std::wcstoul(argv[1], &end, 10);
    if (end == argv[1] || *end != L'\0' || processIdValue == 0 || processIdValue > MAXDWORD)
    {
        std::wcerr << L"Invalid process id\n";
        return 2;
    }

    const std::wstring dllPath = argv[2];
    if (GetFileAttributesW(dllPath.c_str()) == INVALID_FILE_ATTRIBUTES)
    {
        PrintLastError(L"GetFileAttributesW");
        return 2;
    }

    return InjectLibrary(static_cast<DWORD>(processIdValue), dllPath) ? 0 : 1;
}
