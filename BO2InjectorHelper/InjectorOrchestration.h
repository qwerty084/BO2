#pragma once

#include <Windows.h>

#include <string>

namespace BO2InjectorHelper
{
    constexpr DWORD InjectionProcessAccess =
        PROCESS_CREATE_THREAD
        | PROCESS_QUERY_INFORMATION
        | PROCESS_VM_OPERATION
        | PROCESS_VM_WRITE
        | PROCESS_VM_READ;

    struct InjectorOrchestrationOptions
    {
        DWORD WaitMilliseconds = 15000;
    };

    class IInjectorWindowsApi
    {
    public:
        virtual ~IInjectorWindowsApi() = default;

        virtual HANDLE OpenProcess(DWORD desiredAccess, BOOL inheritHandle, DWORD processId) = 0;
        virtual LPVOID VirtualAllocEx(
            HANDLE processHandle,
            LPVOID address,
            SIZE_T size,
            DWORD allocationType,
            DWORD protect) = 0;
        virtual BOOL WriteProcessMemory(
            HANDLE processHandle,
            LPVOID baseAddress,
            LPCVOID buffer,
            SIZE_T size,
            SIZE_T* bytesWritten) = 0;
        virtual HMODULE GetModuleHandleW(LPCWSTR moduleName) = 0;
        virtual FARPROC GetProcAddress(HMODULE moduleHandle, LPCSTR procedureName) = 0;
        virtual HANDLE CreateRemoteThread(
            HANDLE processHandle,
            LPSECURITY_ATTRIBUTES threadAttributes,
            SIZE_T stackSize,
            LPTHREAD_START_ROUTINE startAddress,
            LPVOID parameter,
            DWORD creationFlags,
            LPDWORD threadId) = 0;
        virtual DWORD WaitForSingleObject(HANDLE handle, DWORD milliseconds) = 0;
        virtual BOOL GetExitCodeThread(HANDLE threadHandle, LPDWORD exitCode) = 0;
        virtual BOOL VirtualFreeEx(HANDLE processHandle, LPVOID address, SIZE_T size, DWORD freeType) = 0;
        virtual BOOL CloseHandle(HANDLE handle) = 0;
        virtual HMODULE LoadLibraryExW(LPCWSTR fileName, HANDLE fileHandle, DWORD flags) = 0;
        virtual BOOL FreeLibrary(HMODULE moduleHandle) = 0;
        virtual DWORD GetLastError() = 0;
    };

    class Win32InjectorWindowsApi final : public IInjectorWindowsApi
    {
    public:
        HANDLE OpenProcess(DWORD desiredAccess, BOOL inheritHandle, DWORD processId) override;
        LPVOID VirtualAllocEx(
            HANDLE processHandle,
            LPVOID address,
            SIZE_T size,
            DWORD allocationType,
            DWORD protect) override;
        BOOL WriteProcessMemory(
            HANDLE processHandle,
            LPVOID baseAddress,
            LPCVOID buffer,
            SIZE_T size,
            SIZE_T* bytesWritten) override;
        HMODULE GetModuleHandleW(LPCWSTR moduleName) override;
        FARPROC GetProcAddress(HMODULE moduleHandle, LPCSTR procedureName) override;
        HANDLE CreateRemoteThread(
            HANDLE processHandle,
            LPSECURITY_ATTRIBUTES threadAttributes,
            SIZE_T stackSize,
            LPTHREAD_START_ROUTINE startAddress,
            LPVOID parameter,
            DWORD creationFlags,
            LPDWORD threadId) override;
        DWORD WaitForSingleObject(HANDLE handle, DWORD milliseconds) override;
        BOOL GetExitCodeThread(HANDLE threadHandle, LPDWORD exitCode) override;
        BOOL VirtualFreeEx(HANDLE processHandle, LPVOID address, SIZE_T size, DWORD freeType) override;
        BOOL CloseHandle(HANDLE handle) override;
        HMODULE LoadLibraryExW(LPCWSTR fileName, HANDLE fileHandle, DWORD flags) override;
        BOOL FreeLibrary(HMODULE moduleHandle) override;
        DWORD GetLastError() override;
    };

    void* CalculateRemoteExportAddress(
        DWORD_PTR localModuleBase,
        DWORD_PTR localExportAddress,
        DWORD_PTR remoteModuleBase) noexcept;

    bool ResolveRemoteExportAddress(
        IInjectorWindowsApi& api,
        const std::wstring& dllPath,
        DWORD_PTR remoteModuleBase,
        const char* exportName,
        void*& remoteExportAddress);

    bool StartRemoteMonitor(
        IInjectorWindowsApi& api,
        HANDLE processHandle,
        const std::wstring& dllPath,
        DWORD_PTR remoteModuleBase,
        const InjectorOrchestrationOptions& options = {});

    bool InjectLibrary(
        IInjectorWindowsApi& api,
        DWORD processId,
        const std::wstring& dllPath,
        const InjectorOrchestrationOptions& options = {});
}
