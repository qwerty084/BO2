using BO2.Services.Generated;

namespace BO2.Services
{
    public enum GameCompatibilityState
    {
        Unknown = EventMonitorSnapshotContract.GameCompatibilityStateUnknown,
        WaitingForMonitor = EventMonitorSnapshotContract.GameCompatibilityStateWaitingForMonitor,
        Compatible = EventMonitorSnapshotContract.GameCompatibilityStateCompatible,
        UnsupportedVersion = EventMonitorSnapshotContract.GameCompatibilityStateUnsupportedVersion,
        CaptureDisabled = EventMonitorSnapshotContract.GameCompatibilityStateCaptureDisabled,
        PollingFallback = EventMonitorSnapshotContract.GameCompatibilityStatePollingFallback
    }
}
