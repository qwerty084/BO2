#pragma once

#include "SharedSnapshot.h"

namespace BO2Monitor
{
    GameCompatibilityState TryInstallNotifyHook(SharedSnapshotWriter& snapshotWriter);
    void RunNotifyEventWorker(SharedSnapshotWriter& snapshotWriter);
    void RunPollingFallback(SharedSnapshotWriter& snapshotWriter);
}
