#include "Hook.h"
#include "SharedSnapshot.h"

#include <Windows.h>
#include <memory>

namespace
{
    DWORD WINAPI MonitorThreadProc(LPVOID)
    {
        auto snapshotWriter = std::make_unique<BO2Monitor::SharedSnapshotWriter>();
        if (!snapshotWriter->Initialize())
        {
            return 1;
        }

        BO2Monitor::GameCompatibilityState hookState = BO2Monitor::TryInstallNotifyHook(*snapshotWriter);
        if (hookState == BO2Monitor::GameCompatibilityState::UnsupportedVersion)
        {
            BO2Monitor::RunPollingFallback(*snapshotWriter);
            return 0;
        }

        while (true)
        {
            BO2Monitor::ResolveObservedNotifyNames(*snapshotWriter);
            Sleep(1000);
        }
    }
}

BOOL APIENTRY DllMain(HMODULE moduleHandle, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(moduleHandle);
        HANDLE threadHandle = CreateThread(
            nullptr,
            0,
            MonitorThreadProc,
            nullptr,
            0,
            nullptr);
        if (threadHandle != nullptr)
        {
            CloseHandle(threadHandle);
        }
    }

    return TRUE;
}
