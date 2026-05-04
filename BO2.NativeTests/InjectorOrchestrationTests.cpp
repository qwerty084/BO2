#include "CppUnitTest.h"

#include "InjectorOrchestration.h"

#include <cstring>
#include <string>
#include <vector>

using namespace Microsoft::VisualStudio::CppUnitTestFramework;

namespace BO2NativeTests
{
    namespace
    {
        constexpr DWORD TestProcessId = 4242;
        constexpr DWORD TestWaitMilliseconds = 37;
        constexpr DWORD RemoteModuleBase = 0x50000000;
        constexpr DWORD_PTR LocalModuleBase = 0x10000000;
        constexpr DWORD_PTR LocalStartMonitorAddress = LocalModuleBase + 0x1234;
        constexpr DWORD_PTR ExpectedRemoteStartMonitorAddress = RemoteModuleBase + 0x1234;
        const std::wstring DllPath = L"C:\\Temp\\BO2Monitor.dll";

        class FakeInjectorWindowsApi final : public BO2InjectorHelper::IInjectorWindowsApi
        {
        public:
            FakeInjectorWindowsApi()
                : ProcessHandle(reinterpret_cast<HANDLE>(0x1010)),
                RemotePath(reinterpret_cast<LPVOID>(0x2020)),
                Kernel32Module(reinterpret_cast<HMODULE>(0x3030)),
                LocalMonitorModule(reinterpret_cast<HMODULE>(LocalModuleBase)),
                LoadLibraryAddress(reinterpret_cast<FARPROC>(0x4040)),
                LocalStartMonitorExport(reinterpret_cast<FARPROC>(LocalStartMonitorAddress)),
                LoaderThreadHandle(reinterpret_cast<HANDLE>(0x5050)),
                MonitorThreadHandle(reinterpret_cast<HANDLE>(0x6060))
            {
            }

            HANDLE OpenProcess(DWORD desiredAccess, BOOL inheritHandle, DWORD processId) override
            {
                ++OpenProcessCallCount;
                LastProcessAccess = desiredAccess;
                LastInheritHandle = inheritHandle;
                LastProcessId = processId;
                Events.push_back("open-process");
                return OpenProcessSucceeds ? ProcessHandle : nullptr;
            }

            LPVOID VirtualAllocEx(
                HANDLE processHandle,
                LPVOID address,
                SIZE_T size,
                DWORD allocationType,
                DWORD protect) override
            {
                (void)address;
                ++VirtualAllocExCallCount;
                LastAllocatedProcess = processHandle;
                LastAllocationSize = size;
                LastAllocationType = allocationType;
                LastAllocationProtect = protect;
                Events.push_back("allocate-remote-path");
                return VirtualAllocExSucceeds ? RemotePath : nullptr;
            }

            BOOL WriteProcessMemory(
                HANDLE processHandle,
                LPVOID baseAddress,
                LPCVOID buffer,
                SIZE_T size,
                SIZE_T* bytesWritten) override
            {
                ++WriteProcessMemoryCallCount;
                LastWriteProcess = processHandle;
                LastWriteAddress = baseAddress;
                LastWriteSize = size;
                if (buffer != nullptr && size >= sizeof(wchar_t))
                {
                    WrittenDllPath.assign(
                        static_cast<const wchar_t*>(buffer),
                        (size / sizeof(wchar_t)) - 1);
                }

                if (bytesWritten != nullptr)
                {
                    *bytesWritten = WriteFullBuffer ? size : size - sizeof(wchar_t);
                }

                Events.push_back("write-remote-path");
                return WriteProcessMemorySucceeds ? TRUE : FALSE;
            }

            HMODULE GetModuleHandleW(LPCWSTR moduleName) override
            {
                ++GetModuleHandleCallCount;
                LastModuleName = moduleName == nullptr ? L"" : moduleName;
                Events.push_back("get-kernel32");
                return GetModuleHandleSucceeds ? Kernel32Module : nullptr;
            }

            FARPROC GetProcAddress(HMODULE moduleHandle, LPCSTR procedureName) override
            {
                ++GetProcAddressCallCount;
                LastProcNames.push_back(procedureName == nullptr ? "" : procedureName);
                if (moduleHandle == Kernel32Module && std::strcmp(procedureName, "LoadLibraryW") == 0)
                {
                    Events.push_back("resolve-load-library");
                    return ResolveLoadLibrarySucceeds ? LoadLibraryAddress : nullptr;
                }

                if (moduleHandle == LocalMonitorModule && std::strcmp(procedureName, "StartMonitor") == 0)
                {
                    Events.push_back("resolve-start-monitor");
                    return ResolveStartMonitorSucceeds ? LocalStartMonitorExport : nullptr;
                }

                Events.push_back("resolve-unknown-export");
                return nullptr;
            }

            HANDLE CreateRemoteThread(
                HANDLE processHandle,
                LPSECURITY_ATTRIBUTES threadAttributes,
                SIZE_T stackSize,
                LPTHREAD_START_ROUTINE startAddress,
                LPVOID parameter,
                DWORD creationFlags,
                LPDWORD threadId) override
            {
                (void)threadAttributes;
                (void)stackSize;
                (void)creationFlags;
                (void)threadId;
                ++CreateRemoteThreadCallCount;
                ThreadProcessHandles.push_back(processHandle);
                ThreadStartAddresses.push_back(reinterpret_cast<DWORD_PTR>(startAddress));
                ThreadParameters.push_back(parameter);

                const bool isLoaderThread =
                    reinterpret_cast<DWORD_PTR>(startAddress) == reinterpret_cast<DWORD_PTR>(LoadLibraryAddress);
                Events.push_back(isLoaderThread ? "create-loader-thread" : "create-monitor-thread");
                if (isLoaderThread)
                {
                    return LoaderThreadSucceeds ? LoaderThreadHandle : nullptr;
                }

                return MonitorThreadSucceeds ? MonitorThreadHandle : nullptr;
            }

            DWORD WaitForSingleObject(HANDLE handle, DWORD milliseconds) override
            {
                ++WaitForSingleObjectCallCount;
                WaitedHandles.push_back(handle);
                WaitMilliseconds.push_back(milliseconds);
                if (handle == LoaderThreadHandle)
                {
                    Events.push_back("wait-loader-thread");
                    return LoaderWaitResult;
                }

                Events.push_back("wait-monitor-thread");
                return MonitorWaitResult;
            }

            BOOL GetExitCodeThread(HANDLE threadHandle, LPDWORD exitCode) override
            {
                ++GetExitCodeThreadCallCount;
                ExitCodeThreadHandles.push_back(threadHandle);
                if (threadHandle == LoaderThreadHandle)
                {
                    Events.push_back("read-loader-exit-code");
                    if (!ReadLoaderExitCodeSucceeds)
                    {
                        return FALSE;
                    }

                    *exitCode = LoaderExitCode;
                    return TRUE;
                }

                Events.push_back("read-monitor-exit-code");
                if (!ReadMonitorExitCodeSucceeds)
                {
                    return FALSE;
                }

                *exitCode = MonitorExitCode;
                return TRUE;
            }

            BOOL VirtualFreeEx(HANDLE processHandle, LPVOID address, SIZE_T size, DWORD freeType) override
            {
                ++VirtualFreeExCallCount;
                FreedProcessHandles.push_back(processHandle);
                FreedAddresses.push_back(address);
                FreedSizes.push_back(size);
                FreedTypes.push_back(freeType);
                Events.push_back("free-remote-path");
                return TRUE;
            }

            BOOL CloseHandle(HANDLE handle) override
            {
                ++CloseHandleCallCount;
                ClosedHandles.push_back(handle);
                if (handle == LoaderThreadHandle)
                {
                    Events.push_back("close-loader-thread");
                }
                else if (handle == MonitorThreadHandle)
                {
                    Events.push_back("close-monitor-thread");
                }
                else if (handle == ProcessHandle)
                {
                    Events.push_back("close-process");
                }
                else
                {
                    Events.push_back("close-unknown-handle");
                }

                return TRUE;
            }

            HMODULE LoadLibraryExW(LPCWSTR fileName, HANDLE fileHandle, DWORD flags) override
            {
                (void)fileHandle;
                ++LoadLibraryExCallCount;
                LastLocalLoadPath = fileName == nullptr ? L"" : fileName;
                LastLocalLoadFlags = flags;
                Events.push_back("load-local-monitor-module");
                return LoadLocalModuleSucceeds ? LocalMonitorModule : nullptr;
            }

            BOOL FreeLibrary(HMODULE moduleHandle) override
            {
                ++FreeLibraryCallCount;
                FreedModules.push_back(moduleHandle);
                Events.push_back("free-local-monitor-module");
                return TRUE;
            }

            DWORD GetLastError() override
            {
                ++GetLastErrorCallCount;
                return LastError;
            }

            HANDLE ProcessHandle;
            LPVOID RemotePath;
            HMODULE Kernel32Module;
            HMODULE LocalMonitorModule;
            FARPROC LoadLibraryAddress;
            FARPROC LocalStartMonitorExport;
            HANDLE LoaderThreadHandle;
            HANDLE MonitorThreadHandle;

            bool OpenProcessSucceeds = true;
            bool VirtualAllocExSucceeds = true;
            bool WriteProcessMemorySucceeds = true;
            bool WriteFullBuffer = true;
            bool GetModuleHandleSucceeds = true;
            bool ResolveLoadLibrarySucceeds = true;
            bool LoaderThreadSucceeds = true;
            DWORD LoaderWaitResult = WAIT_OBJECT_0;
            bool ReadLoaderExitCodeSucceeds = true;
            DWORD LoaderExitCode = RemoteModuleBase;
            bool LoadLocalModuleSucceeds = true;
            bool ResolveStartMonitorSucceeds = true;
            bool MonitorThreadSucceeds = true;
            DWORD MonitorWaitResult = WAIT_OBJECT_0;
            bool ReadMonitorExitCodeSucceeds = true;
            DWORD MonitorExitCode = 1;
            DWORD LastError = 12345;

            int OpenProcessCallCount = 0;
            int VirtualAllocExCallCount = 0;
            int WriteProcessMemoryCallCount = 0;
            int GetModuleHandleCallCount = 0;
            int GetProcAddressCallCount = 0;
            int CreateRemoteThreadCallCount = 0;
            int WaitForSingleObjectCallCount = 0;
            int GetExitCodeThreadCallCount = 0;
            int VirtualFreeExCallCount = 0;
            int CloseHandleCallCount = 0;
            int LoadLibraryExCallCount = 0;
            int FreeLibraryCallCount = 0;
            int GetLastErrorCallCount = 0;

            DWORD LastProcessAccess = 0;
            BOOL LastInheritHandle = TRUE;
            DWORD LastProcessId = 0;
            HANDLE LastAllocatedProcess = nullptr;
            SIZE_T LastAllocationSize = 0;
            DWORD LastAllocationType = 0;
            DWORD LastAllocationProtect = 0;
            HANDLE LastWriteProcess = nullptr;
            LPVOID LastWriteAddress = nullptr;
            SIZE_T LastWriteSize = 0;
            std::wstring WrittenDllPath;
            std::wstring LastModuleName;
            std::wstring LastLocalLoadPath;
            DWORD LastLocalLoadFlags = 0;

            std::vector<std::string> Events;
            std::vector<std::string> LastProcNames;
            std::vector<HANDLE> ThreadProcessHandles;
            std::vector<DWORD_PTR> ThreadStartAddresses;
            std::vector<LPVOID> ThreadParameters;
            std::vector<HANDLE> WaitedHandles;
            std::vector<DWORD> WaitMilliseconds;
            std::vector<HANDLE> ExitCodeThreadHandles;
            std::vector<HANDLE> FreedProcessHandles;
            std::vector<LPVOID> FreedAddresses;
            std::vector<SIZE_T> FreedSizes;
            std::vector<DWORD> FreedTypes;
            std::vector<HANDLE> ClosedHandles;
            std::vector<HMODULE> FreedModules;
        };

        bool Inject(FakeInjectorWindowsApi& api)
        {
            const BO2InjectorHelper::InjectorOrchestrationOptions options{ TestWaitMilliseconds };
            return BO2InjectorHelper::InjectLibrary(api, TestProcessId, DllPath, options);
        }

        bool ContainsHandle(const std::vector<HANDLE>& handles, HANDLE expected)
        {
            for (HANDLE handle : handles)
            {
                if (handle == expected)
                {
                    return true;
                }
            }

            return false;
        }

        std::size_t EventIndex(const FakeInjectorWindowsApi& api, const char* expected)
        {
            for (std::size_t index = 0; index < api.Events.size(); ++index)
            {
                if (api.Events[index] == expected)
                {
                    return index;
                }
            }

            return api.Events.size();
        }

        void AssertRemotePathFreed(const FakeInjectorWindowsApi& api)
        {
            Assert::AreEqual(1, api.VirtualFreeExCallCount);
            Assert::IsTrue(api.FreedProcessHandles[0] == api.ProcessHandle);
            Assert::IsTrue(api.FreedAddresses[0] == api.RemotePath);
            Assert::IsTrue(api.FreedSizes[0] == 0);
            Assert::AreEqual(static_cast<DWORD>(MEM_RELEASE), api.FreedTypes[0]);
        }

        void AssertRemotePathNotFreed(const FakeInjectorWindowsApi& api)
        {
            Assert::AreEqual(0, api.VirtualFreeExCallCount);
            Assert::IsTrue(api.FreedAddresses.empty());
        }

        void AssertProcessClosed(const FakeInjectorWindowsApi& api)
        {
            Assert::IsTrue(ContainsHandle(api.ClosedHandles, api.ProcessHandle));
        }

        void AssertLoaderThreadClosed(const FakeInjectorWindowsApi& api)
        {
            Assert::IsTrue(ContainsHandle(api.ClosedHandles, api.LoaderThreadHandle));
        }

        void AssertMonitorThreadClosed(const FakeInjectorWindowsApi& api)
        {
            Assert::IsTrue(ContainsHandle(api.ClosedHandles, api.MonitorThreadHandle));
        }
    }

    TEST_CLASS(InjectorOrchestrationTests)
    {
    public:
        TEST_METHOD(RemoteExportAddressCalculationAppliesLocalOffsetToRemoteBase)
        {
            void* remoteAddress = BO2InjectorHelper::CalculateRemoteExportAddress(
                LocalModuleBase,
                LocalStartMonitorAddress,
                RemoteModuleBase);

            Assert::IsTrue(reinterpret_cast<DWORD_PTR>(remoteAddress) == ExpectedRemoteStartMonitorAddress);
        }

        TEST_METHOD(SuccessfulInjectionLoadsLibraryThenStartsMonitorAtRemoteExportAddress)
        {
            FakeInjectorWindowsApi api;

            const bool succeeded = Inject(api);

            Assert::IsTrue(succeeded);
            Assert::AreEqual(BO2InjectorHelper::InjectionProcessAccess, api.LastProcessAccess);
            Assert::AreEqual(FALSE, api.LastInheritHandle);
            Assert::AreEqual(TestProcessId, api.LastProcessId);
            Assert::IsTrue(api.LastAllocationSize == (DllPath.size() + 1) * sizeof(wchar_t));
            Assert::AreEqual(static_cast<DWORD>(MEM_COMMIT | MEM_RESERVE), api.LastAllocationType);
            Assert::AreEqual(static_cast<DWORD>(PAGE_READWRITE), api.LastAllocationProtect);
            Assert::AreEqual(DllPath.c_str(), api.WrittenDllPath.c_str());
            Assert::AreEqual(L"kernel32.dll", api.LastModuleName.c_str());
            Assert::AreEqual(DllPath.c_str(), api.LastLocalLoadPath.c_str());
            Assert::AreEqual(static_cast<DWORD>(DONT_RESOLVE_DLL_REFERENCES), api.LastLocalLoadFlags);
            Assert::AreEqual(2, api.CreateRemoteThreadCallCount);
            Assert::IsTrue(api.ThreadProcessHandles[0] == api.ProcessHandle);
            Assert::IsTrue(api.ThreadProcessHandles[1] == api.ProcessHandle);
            Assert::IsTrue(api.ThreadStartAddresses[0] == reinterpret_cast<DWORD_PTR>(api.LoadLibraryAddress));
            Assert::IsTrue(api.ThreadParameters[0] == api.RemotePath);
            Assert::IsTrue(api.ThreadStartAddresses[1] == ExpectedRemoteStartMonitorAddress);
            Assert::IsTrue(api.ThreadParameters[1] == nullptr);
            Assert::IsTrue(api.WaitedHandles[0] == api.LoaderThreadHandle);
            Assert::IsTrue(api.WaitedHandles[1] == api.MonitorThreadHandle);
            Assert::AreEqual(TestWaitMilliseconds, api.WaitMilliseconds[0]);
            Assert::AreEqual(TestWaitMilliseconds, api.WaitMilliseconds[1]);
            Assert::IsTrue(EventIndex(api, "read-loader-exit-code") < EventIndex(api, "create-monitor-thread"));
            AssertRemotePathFreed(api);
            AssertLoaderThreadClosed(api);
            AssertMonitorThreadClosed(api);
            AssertProcessClosed(api);
            Assert::AreEqual(1, api.FreeLibraryCallCount);
        }

        TEST_METHOD(OpenProcessFailureDoesNotCleanupMissingHandles)
        {
            FakeInjectorWindowsApi api;
            api.OpenProcessSucceeds = false;

            const bool succeeded = Inject(api);

            Assert::IsFalse(succeeded);
            Assert::AreEqual(1, api.OpenProcessCallCount);
            Assert::AreEqual(0, api.VirtualAllocExCallCount);
            AssertRemotePathNotFreed(api);
            Assert::AreEqual(0, api.CloseHandleCallCount);
        }

        TEST_METHOD(RemoteAllocationFailureClosesProcessWithoutFreeingMissingAllocation)
        {
            FakeInjectorWindowsApi api;
            api.VirtualAllocExSucceeds = false;

            const bool succeeded = Inject(api);

            Assert::IsFalse(succeeded);
            Assert::AreEqual(1, api.VirtualAllocExCallCount);
            AssertRemotePathNotFreed(api);
            Assert::AreEqual(1, api.CloseHandleCallCount);
            AssertProcessClosed(api);
        }

        TEST_METHOD(RemoteWriteFailureFreesPathAndClosesProcess)
        {
            FakeInjectorWindowsApi api;
            api.WriteFullBuffer = false;

            const bool succeeded = Inject(api);

            Assert::IsFalse(succeeded);
            Assert::AreEqual(1, api.WriteProcessMemoryCallCount);
            AssertRemotePathFreed(api);
            Assert::AreEqual(1, api.CloseHandleCallCount);
            AssertProcessClosed(api);
        }

        TEST_METHOD(LoaderThreadCreationFailureFreesPathAndClosesProcess)
        {
            FakeInjectorWindowsApi api;
            api.LoaderThreadSucceeds = false;

            const bool succeeded = Inject(api);

            Assert::IsFalse(succeeded);
            Assert::AreEqual(1, api.CreateRemoteThreadCallCount);
            AssertRemotePathFreed(api);
            Assert::AreEqual(1, api.CloseHandleCallCount);
            AssertProcessClosed(api);
        }

        TEST_METHOD(LoaderWaitTimeoutClosesThreadAndProcessWithoutFreeingPath)
        {
            FakeInjectorWindowsApi api;
            api.LoaderWaitResult = WAIT_TIMEOUT;

            const bool succeeded = Inject(api);

            Assert::IsFalse(succeeded);
            Assert::AreEqual(1, api.WaitForSingleObjectCallCount);
            Assert::AreEqual(0, api.GetExitCodeThreadCallCount);
            AssertRemotePathNotFreed(api);
            AssertLoaderThreadClosed(api);
            AssertProcessClosed(api);
            Assert::AreEqual(2, api.CloseHandleCallCount);
        }

        TEST_METHOD(LoaderExitCodeReadFailureFreesPathAndClosesThreadAndProcess)
        {
            FakeInjectorWindowsApi api;
            api.ReadLoaderExitCodeSucceeds = false;

            const bool succeeded = Inject(api);

            Assert::IsFalse(succeeded);
            Assert::AreEqual(1, api.GetExitCodeThreadCallCount);
            AssertRemotePathFreed(api);
            AssertLoaderThreadClosed(api);
            AssertProcessClosed(api);
            Assert::AreEqual(2, api.CloseHandleCallCount);
        }

        TEST_METHOD(ExportResolutionFailureFreesPathAndClosesLoaderThreadAndProcess)
        {
            FakeInjectorWindowsApi api;
            api.ResolveStartMonitorSucceeds = false;

            const bool succeeded = Inject(api);

            Assert::IsFalse(succeeded);
            Assert::AreEqual(1, api.LoadLibraryExCallCount);
            Assert::AreEqual(2, api.GetProcAddressCallCount);
            Assert::AreEqual(1, api.FreeLibraryCallCount);
            Assert::IsTrue(api.FreedModules[0] == api.LocalMonitorModule);
            Assert::AreEqual(1, api.CreateRemoteThreadCallCount);
            AssertRemotePathFreed(api);
            AssertLoaderThreadClosed(api);
            AssertProcessClosed(api);
            Assert::AreEqual(2, api.CloseHandleCallCount);
        }

        TEST_METHOD(MonitorThreadCreationFailureFreesPathAndClosesLoaderThreadAndProcess)
        {
            FakeInjectorWindowsApi api;
            api.MonitorThreadSucceeds = false;

            const bool succeeded = Inject(api);

            Assert::IsFalse(succeeded);
            Assert::AreEqual(2, api.CreateRemoteThreadCallCount);
            Assert::AreEqual(1, api.FreeLibraryCallCount);
            AssertRemotePathFreed(api);
            AssertLoaderThreadClosed(api);
            AssertProcessClosed(api);
            Assert::AreEqual(2, api.CloseHandleCallCount);
        }

        TEST_METHOD(MonitorWaitFailureClosesMonitorThreadAndCleansUp)
        {
            FakeInjectorWindowsApi api;
            api.MonitorWaitResult = WAIT_TIMEOUT;

            const bool succeeded = Inject(api);

            Assert::IsFalse(succeeded);
            Assert::AreEqual(2, api.WaitForSingleObjectCallCount);
            AssertRemotePathFreed(api);
            AssertMonitorThreadClosed(api);
            AssertLoaderThreadClosed(api);
            AssertProcessClosed(api);
            Assert::AreEqual(3, api.CloseHandleCallCount);
        }

        TEST_METHOD(MonitorExitCodeReadFailureClosesMonitorThreadAndCleansUp)
        {
            FakeInjectorWindowsApi api;
            api.ReadMonitorExitCodeSucceeds = false;

            const bool succeeded = Inject(api);

            Assert::IsFalse(succeeded);
            Assert::AreEqual(2, api.GetExitCodeThreadCallCount);
            AssertRemotePathFreed(api);
            AssertMonitorThreadClosed(api);
            AssertLoaderThreadClosed(api);
            AssertProcessClosed(api);
            Assert::AreEqual(3, api.CloseHandleCallCount);
        }
    };
}
