#pragma once

#include "SharedSnapshot.h"

namespace BO2Monitor
{
    GameEventType MapNotifyName(const char* notifyName);
    GameCompatibilityState TryInstallNotifyHook(SharedSnapshotWriter& snapshotWriter);
    void ResolveObservedNotifyNames(SharedSnapshotWriter& snapshotWriter);
    void RunPollingFallback(SharedSnapshotWriter& snapshotWriter);
}
