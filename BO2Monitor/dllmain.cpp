#include "Hook.h"
#include "SharedSnapshot.h"

#include <Windows.h>
#include <atomic>
#include <memory>

#if defined(_M_IX86)
#pragma comment(linker, "/EXPORT:StartMonitor=_StartMonitor@4")
#define BO2MONITOR_START_EXPORT
#else
#define BO2MONITOR_START_EXPORT __declspec(dllexport)
#endif

namespace
{
    std::atomic_bool monitorStarted{ false };

    DWORD WINAPI MonitorThreadProc(LPVOID)
    {
        auto snapshotWriter = std::make_unique<BO2Monitor::SharedSnapshotWriter>();
        if (!snapshotWriter->Initialize())
        {
            monitorStarted.store(false);
            return 1;
        }

        BO2Monitor::GameCompatibilityState hookState = BO2Monitor::TryInstallNotifyHook(*snapshotWriter);
        if (hookState == BO2Monitor::GameCompatibilityState::UnsupportedVersion)
        {
            BO2Monitor::RunPollingFallback(*snapshotWriter);
            monitorStarted.store(false);
            return 0;
        }

        BO2Monitor::RunNotifyEventWorker(*snapshotWriter);
        monitorStarted.store(false);
        return 0;
    }
}

extern "C" BO2MONITOR_START_EXPORT DWORD WINAPI StartMonitor(void*)
{
    bool expected = false;
    if (!monitorStarted.compare_exchange_strong(expected, true))
    {
        return 1;
    }

    HANDLE threadHandle = CreateThread(
        nullptr,
        0,
        MonitorThreadProc,
        nullptr,
        0,
        nullptr);
    if (threadHandle == nullptr)
    {
        monitorStarted.store(false);
        return 0;
    }

    CloseHandle(threadHandle);
    return 1;
}

BOOL APIENTRY DllMain(HMODULE moduleHandle, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        DisableThreadLibraryCalls(moduleHandle);
    }

    return TRUE;
}
