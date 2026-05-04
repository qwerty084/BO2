using BO2.Services.Generated;

namespace BO2.Services
{
    public enum GameEventType
    {
        Unknown = EventMonitorSnapshotContract.GameEventTypeUnknown,
        StartOfRound = EventMonitorSnapshotContract.GameEventTypeStartOfRound,
        EndOfRound = EventMonitorSnapshotContract.GameEventTypeEndOfRound,
        PowerUpGrabbed = EventMonitorSnapshotContract.GameEventTypePowerUpGrabbed,
        DogRoundStarting = EventMonitorSnapshotContract.GameEventTypeDogRoundStarting,
        PowerOn = EventMonitorSnapshotContract.GameEventTypePowerOn,
        EndGame = EventMonitorSnapshotContract.GameEventTypeEndGame,
        PerkBought = EventMonitorSnapshotContract.GameEventTypePerkBought,
        RoundChanged = EventMonitorSnapshotContract.GameEventTypeRoundChanged,
        PointsChanged = EventMonitorSnapshotContract.GameEventTypePointsChanged,
        KillsChanged = EventMonitorSnapshotContract.GameEventTypeKillsChanged,
        DownsChanged = EventMonitorSnapshotContract.GameEventTypeDownsChanged,
        NotifyCandidateRejected = EventMonitorSnapshotContract.GameEventTypeNotifyCandidateRejected,
        NotifyEntryCandidate = EventMonitorSnapshotContract.GameEventTypeNotifyEntryCandidate,
        StringResolverCandidate = EventMonitorSnapshotContract.GameEventTypeStringResolverCandidate,
        NotifyObserved = EventMonitorSnapshotContract.GameEventTypeNotifyObserved,
        BoxEvent = EventMonitorSnapshotContract.GameEventTypeBoxEvent
    }
}
