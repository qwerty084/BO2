using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BO2.Services
{
    public enum GameHistoryRecordingState
    {
        Unavailable,
        WaitingForRoundOne,
        Recording,
        Discarded,
        SavePending,
        FailedSave,
        Saved
    }

    public enum GameHistoryRecordingStatusKind
    {
        WaitingForConnection,
        ActiveRecording,
        RequiresSupportedMap,
        RequiresHookBackedEventMonitor,
        DiscardedSequenceOrDroppedLifecycle,
        DiscardedMissingStats,
        DiscardedConnectionEndedBeforeSave,
        SavePending,
        FailedSave
    }

    public enum GameHistoryRecordingUnavailableReason
    {
        None,
        NotConnected,
        RequiresSupportedMap,
        MissingMapIdentity,
        MissingFriendlyMapName,
        RequiresHookBackedEventMonitor
    }

    public enum GameHistoryRecordingDiscardReason
    {
        None,
        PollingFallback,
        SequenceGap,
        DroppedLifecycleData,
        MissingRequiredStats,
        MissingMapIdentity,
        UnsupportedMapIdentity,
        MissingFriendlyMapName,
        DetectedGameChanged,
        Disconnected,
        AppClosed
    }

    public sealed record GameHistoryRecordingStatus(
        GameHistoryRecordingState State,
        GameHistoryRecordingUnavailableReason UnavailableReason,
        GameHistoryRecordingDiscardReason DiscardReason,
        int? ActiveRoundNumber,
        string? LastSavedHistoryId,
        string? MapName = null,
        string? SaveErrorMessage = null)
    {
        public GameHistoryRecordingStatusKind Kind => State switch
        {
            GameHistoryRecordingState.Recording => GameHistoryRecordingStatusKind.ActiveRecording,
            GameHistoryRecordingState.SavePending => GameHistoryRecordingStatusKind.SavePending,
            GameHistoryRecordingState.FailedSave => GameHistoryRecordingStatusKind.FailedSave,
            GameHistoryRecordingState.Discarded => DiscardReason switch
            {
                GameHistoryRecordingDiscardReason.SequenceGap
                    or GameHistoryRecordingDiscardReason.DroppedLifecycleData
                    or GameHistoryRecordingDiscardReason.PollingFallback => GameHistoryRecordingStatusKind.DiscardedSequenceOrDroppedLifecycle,
                GameHistoryRecordingDiscardReason.MissingRequiredStats => GameHistoryRecordingStatusKind.DiscardedMissingStats,
                _ => GameHistoryRecordingStatusKind.DiscardedConnectionEndedBeforeSave
            },
            GameHistoryRecordingState.Unavailable => UnavailableReason switch
            {
                GameHistoryRecordingUnavailableReason.RequiresHookBackedEventMonitor => GameHistoryRecordingStatusKind.RequiresHookBackedEventMonitor,
                GameHistoryRecordingUnavailableReason.RequiresSupportedMap
                    or GameHistoryRecordingUnavailableReason.MissingMapIdentity
                    or GameHistoryRecordingUnavailableReason.MissingFriendlyMapName => GameHistoryRecordingStatusKind.RequiresSupportedMap,
                _ => GameHistoryRecordingStatusKind.WaitingForConnection
            },
            _ => GameHistoryRecordingStatusKind.WaitingForConnection
        };

        public static GameHistoryRecordingStatus WaitingForConnection { get; } =
            Unavailable(GameHistoryRecordingUnavailableReason.NotConnected);

        public static GameHistoryRecordingStatus RequiresSupportedMap { get; } =
            Unavailable(GameHistoryRecordingUnavailableReason.RequiresSupportedMap);

        public static GameHistoryRecordingStatus RequiresHookBackedEventMonitor { get; } =
            Unavailable(GameHistoryRecordingUnavailableReason.RequiresHookBackedEventMonitor);

        public static GameHistoryRecordingStatus DiscardedSequenceOrDroppedLifecycle { get; } =
            Discarded(GameHistoryRecordingDiscardReason.SequenceGap);

        public static GameHistoryRecordingStatus DiscardedMissingStats { get; } =
            Discarded(GameHistoryRecordingDiscardReason.MissingRequiredStats);

        public static GameHistoryRecordingStatus DiscardedConnectionEndedBeforeSave { get; } =
            Discarded(GameHistoryRecordingDiscardReason.Disconnected);

        public static GameHistoryRecordingStatus Active(string? mapName)
        {
            return new GameHistoryRecordingStatus(
                GameHistoryRecordingState.Recording,
                GameHistoryRecordingUnavailableReason.None,
                GameHistoryRecordingDiscardReason.None,
                null,
                null,
                mapName);
        }

        public static GameHistoryRecordingStatus Unavailable(GameHistoryRecordingUnavailableReason reason)
        {
            return new GameHistoryRecordingStatus(
                GameHistoryRecordingState.Unavailable,
                reason,
                GameHistoryRecordingDiscardReason.None,
                null,
                null,
                null);
        }

        public static GameHistoryRecordingStatus WaitingForRoundOne(string? mapName = null)
        {
            return new GameHistoryRecordingStatus(
                GameHistoryRecordingState.WaitingForRoundOne,
                GameHistoryRecordingUnavailableReason.None,
                GameHistoryRecordingDiscardReason.None,
                null,
                null,
                mapName);
        }

        public static GameHistoryRecordingStatus Recording(int activeRoundNumber, string? mapName = null)
        {
            return new GameHistoryRecordingStatus(
                GameHistoryRecordingState.Recording,
                GameHistoryRecordingUnavailableReason.None,
                GameHistoryRecordingDiscardReason.None,
                activeRoundNumber,
                null,
                mapName);
        }

        public static GameHistoryRecordingStatus Discarded(GameHistoryRecordingDiscardReason reason)
        {
            return new GameHistoryRecordingStatus(
                GameHistoryRecordingState.Discarded,
                GameHistoryRecordingUnavailableReason.None,
                reason,
                null,
                null,
                null);
        }

        public static GameHistoryRecordingStatus Saved(string historyId, string? mapName = null)
        {
            return new GameHistoryRecordingStatus(
                GameHistoryRecordingState.Saved,
                GameHistoryRecordingUnavailableReason.None,
                GameHistoryRecordingDiscardReason.None,
                null,
                historyId,
                mapName);
        }

        public static GameHistoryRecordingStatus SavePending(string historyId, string? mapName = null)
        {
            return new GameHistoryRecordingStatus(
                GameHistoryRecordingState.SavePending,
                GameHistoryRecordingUnavailableReason.None,
                GameHistoryRecordingDiscardReason.None,
                null,
                historyId,
                mapName);
        }

        public static GameHistoryRecordingStatus FailedSave(
            string historyId,
            string? mapName = null,
            string? errorMessage = null)
        {
            return new GameHistoryRecordingStatus(
                GameHistoryRecordingState.FailedSave,
                GameHistoryRecordingUnavailableReason.None,
                GameHistoryRecordingDiscardReason.None,
                null,
                historyId,
                mapName,
                errorMessage);
        }
    }

    internal sealed class GameHistoryRecorder
    {
        private CandidateSession? _candidate;
        private DetectedGame? _observedGame;
        private ulong? _lastObservedEventSequence;
        private uint _lastDroppedEventCount;
        private uint _lastDroppedNotifyCount;

        public GameHistoryRecorder()
        {
            Status = GameHistoryRecordingStatus.Unavailable(GameHistoryRecordingUnavailableReason.NotConnected);
        }

        public GameHistoryRecordingStatus Status { get; private set; }

        public GameHistoryEntry? ObserveSnapshot(GameConnectionSnapshot snapshot)
        {
            if (HasDetectedGameChanged(snapshot.CurrentGame))
            {
                ResetEventTracking();
                if (_candidate is not null)
                {
                    Discard(GameHistoryRecordingDiscardReason.DetectedGameChanged);
                    _observedGame = snapshot.CurrentGame;
                    return null;
                }
            }

            _observedGame = snapshot.CurrentGame;

            if (snapshot.ConnectionPhase != GameConnectionPhase.Connected)
            {
                ResetEventTracking();
                if (_candidate is not null)
                {
                    Discard(GameHistoryRecordingDiscardReason.Disconnected);
                    return null;
                }

                Status = GameHistoryRecordingStatus.Unavailable(GameHistoryRecordingUnavailableReason.NotConnected);
                return null;
            }

            GameConnectionEventMonitorSummary eventMonitorSummary = snapshot.EventMonitorSummary;
            if (eventMonitorSummary.State == GameConnectionEventMonitorState.PollingFallback)
            {
                ResetEventTracking();
                if (_candidate is not null)
                {
                    Discard(GameHistoryRecordingDiscardReason.PollingFallback);
                    return null;
                }

                Status = GameHistoryRecordingStatus.Unavailable(
                    GameHistoryRecordingUnavailableReason.RequiresHookBackedEventMonitor);
                return null;
            }

            if (eventMonitorSummary.State != GameConnectionEventMonitorState.Ready
                || eventMonitorSummary.Status.CompatibilityState != GameCompatibilityState.Compatible)
            {
                if (_candidate is not null)
                {
                    Discard(GameHistoryRecordingDiscardReason.DroppedLifecycleData);
                    return null;
                }

                Status = GameHistoryRecordingStatus.Unavailable(
                    GameHistoryRecordingUnavailableReason.RequiresHookBackedEventMonitor);
                return null;
            }

            if (!TryGetSupportedMap(
                snapshot.MapIdentityResult,
                out GameMapIdentity? mapIdentity,
                out GameHistoryRecordingUnavailableReason unavailableReason,
                out GameHistoryRecordingDiscardReason discardReason))
            {
                if (_candidate is not null)
                {
                    Discard(discardReason);
                    return null;
                }

                Status = GameHistoryRecordingStatus.Unavailable(unavailableReason);
                return null;
            }

            GameHistoryEventBatch eventBatch = ReadNewEvents(eventMonitorSummary.Status);
            if (HasDroppedLifecycleData(eventMonitorSummary.Status))
            {
                if (_candidate is not null)
                {
                    Discard(GameHistoryRecordingDiscardReason.DroppedLifecycleData);
                    return null;
                }

                Status = GameHistoryRecordingStatus.Unavailable(
                    GameHistoryRecordingUnavailableReason.RequiresHookBackedEventMonitor);
                return null;
            }

            if (eventBatch.HasSequenceGap)
            {
                if (_candidate is not null)
                {
                    Discard(GameHistoryRecordingDiscardReason.SequenceGap);
                    return null;
                }

                Status = GameHistoryRecordingStatus.Unavailable(
                    GameHistoryRecordingUnavailableReason.RequiresHookBackedEventMonitor);
                return null;
            }

            GameHistoryStats? observedStats = TryReadRequiredStats(snapshot.ReadResult);
            TimeSpan? gameDuration = snapshot.TimerDisplayState?.GameTime?.Duration;
            TimeSpan? roundDuration = snapshot.TimerDisplayState?.RoundTime?.Duration;

            if (_candidate is not null)
            {
                _candidate.ObserveStatsAndGameDuration(observedStats, gameDuration);
                TryCaptureActiveRoundBaseline(_candidate, observedStats);
            }

            foreach (GameEvent gameEvent in eventBatch.Events)
            {
                GameHistoryEntry? completedEntry =
                    ProcessEvent(snapshot, gameEvent, mapIdentity, observedStats, gameDuration, roundDuration);
                if (Status.State == GameHistoryRecordingState.Discarded)
                {
                    return null;
                }

                if (completedEntry is not null)
                {
                    return completedEntry;
                }
            }

            if (_candidate is not null)
            {
                Status = GameHistoryRecordingStatus.Recording(
                    _candidate.ActiveRound?.RoundNumber ?? 0,
                    _candidate.MapIdentity.DisplayName);
            }
            else if (Status.State is not GameHistoryRecordingState.Saved
                and not GameHistoryRecordingState.SavePending
                and not GameHistoryRecordingState.FailedSave)
            {
                Status = GameHistoryRecordingStatus.WaitingForRoundOne(mapIdentity.DisplayName);
            }

            return null;
        }

        public void DiscardForAppClose()
        {
            if (_candidate is not null)
            {
                Discard(GameHistoryRecordingDiscardReason.AppClosed);
            }
        }

        public void MarkCompletedEntrySaved(GameHistoryEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            if (Status.State == GameHistoryRecordingState.SavePending
                && string.Equals(Status.LastSavedHistoryId, entry.Id, StringComparison.Ordinal))
            {
                Status = GameHistoryRecordingStatus.Saved(entry.Id, entry.MapIdentity.FriendlyName);
            }
        }

        public void MarkCompletedEntrySaveFailed(GameHistoryEntry entry, string? errorMessage = null)
        {
            ArgumentNullException.ThrowIfNull(entry);

            if (Status.State == GameHistoryRecordingState.SavePending
                && string.Equals(Status.LastSavedHistoryId, entry.Id, StringComparison.Ordinal))
            {
                Status = GameHistoryRecordingStatus.FailedSave(
                    entry.Id,
                    entry.MapIdentity.FriendlyName,
                    errorMessage);
            }
        }

        private GameHistoryEntry? ProcessEvent(
            GameConnectionSnapshot snapshot,
            GameEvent gameEvent,
            GameMapIdentity mapIdentity,
            GameHistoryStats? observedStats,
            TimeSpan? gameDuration,
            TimeSpan? roundDuration)
        {
            switch (gameEvent.EventType)
            {
                case GameEventType.StartOfRound:
                    HandleRoundStart(snapshot, gameEvent, mapIdentity, observedStats, gameDuration);
                    break;
                case GameEventType.EndOfRound:
                    HandleRoundEnd(gameEvent, observedStats, roundDuration);
                    break;
                case GameEventType.EndGame:
                    return HandleEndGame(gameEvent, observedStats, gameDuration, roundDuration);
                case GameEventType.BoxEvent:
                    HandleBoxEvent(gameEvent);
                    break;
            }

            return null;
        }

        private void HandleRoundStart(
            GameConnectionSnapshot snapshot,
            GameEvent gameEvent,
            GameMapIdentity mapIdentity,
            GameHistoryStats? observedStats,
            TimeSpan? gameDuration)
        {
            if (gameEvent.LevelTime <= 0)
            {
                return;
            }

            if (_candidate is null)
            {
                if (gameEvent.LevelTime != 1 || snapshot.CurrentGame is null)
                {
                    return;
                }

                _candidate = new CandidateSession(
                    snapshot.CurrentGame,
                    mapIdentity,
                    gameEvent.ReceivedAt);
                _candidate.ObserveStatsAndGameDuration(observedStats, gameDuration);
                _candidate.StartRound(gameEvent.LevelTime, gameEvent.ReceivedAt);
                TryCaptureActiveRoundBaseline(_candidate, observedStats);
                Status = GameHistoryRecordingStatus.Recording(gameEvent.LevelTime, mapIdentity.DisplayName);
                return;
            }

            if (_candidate.ActiveRound is not null)
            {
                Discard(GameHistoryRecordingDiscardReason.SequenceGap);
                return;
            }

            _candidate.StartRound(gameEvent.LevelTime, gameEvent.ReceivedAt);
            TryCaptureActiveRoundBaseline(_candidate, observedStats);
        }

        private void HandleRoundEnd(
            GameEvent gameEvent,
            GameHistoryStats? observedStats,
            TimeSpan? roundDuration)
        {
            if (_candidate is null)
            {
                return;
            }

            if (!TryCloseActiveRound(gameEvent.ReceivedAt, observedStats, roundDuration))
            {
                Discard(GameHistoryRecordingDiscardReason.MissingRequiredStats);
            }
        }

        private GameHistoryEntry? HandleEndGame(
            GameEvent gameEvent,
            GameHistoryStats? observedStats,
            TimeSpan? gameDuration,
            TimeSpan? roundDuration)
        {
            if (_candidate is null)
            {
                return null;
            }

            GameHistoryStats? finalStats = observedStats ?? _candidate.LatestStats;
            if (_candidate.ActiveRound is not null
                && !TryCloseActiveRound(gameEvent.ReceivedAt, finalStats, roundDuration))
            {
                Discard(GameHistoryRecordingDiscardReason.MissingRequiredStats);
                return null;
            }

            finalStats ??= _candidate.LatestStats;
            if (finalStats is null || _candidate.Rounds.Count == 0)
            {
                Discard(GameHistoryRecordingDiscardReason.MissingRequiredStats);
                return null;
            }

            GameHistoryEntry entry = _candidate.ToEntry(
                gameEvent.ReceivedAt,
                finalStats,
                gameDuration ?? _candidate.LatestGameDuration);
            _candidate = null;
            Status = GameHistoryRecordingStatus.SavePending(entry.Id, entry.MapIdentity.FriendlyName);
            return entry;
        }

        private void HandleBoxEvent(GameEvent gameEvent)
        {
            if (_candidate?.ActiveRound is not ActiveRound activeRound)
            {
                return;
            }

            string? rawWeaponToken = string.IsNullOrWhiteSpace(gameEvent.WeaponName)
                ? null
                : gameEvent.WeaponName.Trim();
            _candidate.BoxEvents.Add(new GameHistoryBoxEvent
            {
                ReceivedAt = gameEvent.ReceivedAt,
                RoundNumber = activeRound.RoundNumber,
                EventName = gameEvent.EventName,
                RawWeaponToken = rawWeaponToken,
                WeaponDisplayName = ResolveKnownWeaponDisplayName(rawWeaponToken),
                OwnerId = gameEvent.OwnerId,
                StringValue = gameEvent.StringValue
            });
        }

        private bool TryCloseActiveRound(
            DateTimeOffset endedAt,
            GameHistoryStats? observedStats,
            TimeSpan? roundDuration)
        {
            if (_candidate?.ActiveRound is not ActiveRound activeRound)
            {
                return true;
            }

            TryCaptureActiveRoundBaseline(_candidate, observedStats);
            if (activeRound.BaselineStats is null || observedStats is null)
            {
                return false;
            }

            _candidate.Rounds.Add(new GameHistoryRound
            {
                RoundNumber = activeRound.RoundNumber,
                StartedAt = activeRound.StartedAt,
                EndedAt = endedAt,
                CumulativeStats = observedStats,
                DeltaStats = GameHistoryStats.Subtract(observedStats, activeRound.BaselineStats),
                RoundDuration = roundDuration
            });
            _candidate.ActiveRound = null;
            _candidate.LatestStats = observedStats;
            return true;
        }

        private static void TryCaptureActiveRoundBaseline(
            CandidateSession candidate,
            GameHistoryStats? observedStats)
        {
            if (candidate.ActiveRound is ActiveRound activeRound
                && activeRound.BaselineStats is null
                && observedStats is not null)
            {
                activeRound.BaselineStats = observedStats;
            }
        }

        private static GameHistoryStats? TryReadRequiredStats(PlayerStatsReadResult? readResult)
        {
            return readResult?.Stats is PlayerStats stats
                ? GameHistoryStats.FromPlayerStats(stats)
                : null;
        }

        private static string? ResolveKnownWeaponDisplayName(string? rawWeaponToken)
        {
            if (string.IsNullOrWhiteSpace(rawWeaponToken))
            {
                return null;
            }

            string displayName = WeaponDisplayNameResolver.ResolveDisplayName(rawWeaponToken);
            return string.Equals(displayName, rawWeaponToken.Trim(), StringComparison.OrdinalIgnoreCase)
                ? null
                : displayName;
        }

        private bool HasDetectedGameChanged(DetectedGame? currentGame)
        {
            if (_observedGame is null || currentGame is null)
            {
                return _observedGame is not null || currentGame is not null;
            }

            return _observedGame.ProcessId != currentGame.ProcessId
                || _observedGame.Variant != currentGame.Variant
                || !string.Equals(_observedGame.ProcessName, currentGame.ProcessName, StringComparison.Ordinal);
        }

        private static bool TryGetSupportedMap(
            GameMapIdentityReadResult? mapIdentityResult,
            out GameMapIdentity mapIdentity,
            out GameHistoryRecordingUnavailableReason unavailableReason,
            out GameHistoryRecordingDiscardReason discardReason)
        {
            mapIdentity = null!;
            unavailableReason = GameHistoryRecordingUnavailableReason.MissingMapIdentity;
            discardReason = GameHistoryRecordingDiscardReason.MissingMapIdentity;

            if (mapIdentityResult is null)
            {
                return false;
            }

            if (mapIdentityResult.Status == GameMapIdentityReadStatus.SupportedMap
                && mapIdentityResult.Identity is GameMapIdentity identity)
            {
                if (string.IsNullOrWhiteSpace(identity.DisplayName))
                {
                    unavailableReason = GameHistoryRecordingUnavailableReason.MissingFriendlyMapName;
                    discardReason = GameHistoryRecordingDiscardReason.MissingFriendlyMapName;
                    return false;
                }

                mapIdentity = identity;
                return true;
            }

            switch (mapIdentityResult.Status)
            {
                case GameMapIdentityReadStatus.UnsupportedMapIdentity:
                case GameMapIdentityReadStatus.UnsupportedVariant:
                    unavailableReason = GameHistoryRecordingUnavailableReason.RequiresSupportedMap;
                    discardReason = GameHistoryRecordingDiscardReason.UnsupportedMapIdentity;
                    break;
                case GameMapIdentityReadStatus.Malformed:
                case GameMapIdentityReadStatus.MissingMapIdentity:
                case GameMapIdentityReadStatus.Unreadable:
                default:
                    unavailableReason = GameHistoryRecordingUnavailableReason.MissingMapIdentity;
                    discardReason = GameHistoryRecordingDiscardReason.MissingMapIdentity;
                    break;
            }

            return false;
        }

        private GameHistoryEventBatch ReadNewEvents(GameEventMonitorStatus eventStatus)
        {
            GameEvent[] sequencedEvents = [.. eventStatus.RecentEvents
                .Where(static gameEvent => gameEvent.Sequence > 0)
                .OrderBy(static gameEvent => gameEvent.Sequence)];
            if (sequencedEvents.Length == 0)
            {
                return GameHistoryEventBatch.Empty;
            }

            ulong nextExpectedSequence = _lastObservedEventSequence is ulong lastSequence
                ? lastSequence + 1UL
                : 1UL;
            bool hasSequenceGap = false;
            List<GameEvent> newEvents = [];

            foreach (GameEvent gameEvent in sequencedEvents)
            {
                if (_lastObservedEventSequence is ulong lastObservedSequence
                    && gameEvent.Sequence <= lastObservedSequence)
                {
                    continue;
                }

                if (gameEvent.Sequence > nextExpectedSequence)
                {
                    hasSequenceGap = true;
                }

                nextExpectedSequence = gameEvent.Sequence + 1UL;
                newEvents.Add(gameEvent);
            }

            _lastObservedEventSequence = _lastObservedEventSequence is ulong observedSequence
                ? Math.Max(observedSequence, sequencedEvents[^1].Sequence)
                : sequencedEvents[^1].Sequence;

            return new GameHistoryEventBatch(newEvents, hasSequenceGap);
        }

        private bool HasDroppedLifecycleData(GameEventMonitorStatus eventStatus)
        {
            bool hasDroppedData = eventStatus.DroppedEventCount > _lastDroppedEventCount
                || eventStatus.DroppedNotifyCount > _lastDroppedNotifyCount;
            _lastDroppedEventCount = eventStatus.DroppedEventCount;
            _lastDroppedNotifyCount = eventStatus.DroppedNotifyCount;
            return hasDroppedData;
        }

        private void Discard(GameHistoryRecordingDiscardReason reason)
        {
            _candidate = null;
            Status = GameHistoryRecordingStatus.Discarded(reason);
        }

        private void ResetEventTracking()
        {
            _lastObservedEventSequence = null;
            _lastDroppedEventCount = 0;
            _lastDroppedNotifyCount = 0;
        }

        private sealed record GameHistoryEventBatch(
            IReadOnlyList<GameEvent> Events,
            bool HasSequenceGap)
        {
            public static GameHistoryEventBatch Empty { get; } = new(
                [],
                HasSequenceGap: false);
        }

        private sealed class CandidateSession(
            DetectedGame detectedGame,
            GameMapIdentity mapIdentity,
            DateTimeOffset startedAt)
        {
            public DetectedGame DetectedGame { get; } = detectedGame;

            public GameMapIdentity MapIdentity { get; } = mapIdentity;

            public DateTimeOffset StartedAt { get; } = startedAt;

            public ActiveRound? ActiveRound { get; set; }

            public GameHistoryStats? LatestStats { get; set; }

            public TimeSpan? LatestGameDuration { get; private set; }

            public List<GameHistoryRound> Rounds { get; } = [];

            public List<GameHistoryBoxEvent> BoxEvents { get; } = [];

            public void StartRound(int roundNumber, DateTimeOffset startedAt)
            {
                ActiveRound = new ActiveRound(roundNumber, startedAt);
            }

            public void ObserveStatsAndGameDuration(GameHistoryStats? stats, TimeSpan? gameDuration)
            {
                if (stats is not null)
                {
                    LatestStats = stats;
                }

                if (gameDuration is not null)
                {
                    LatestGameDuration = gameDuration;
                }
            }

            public GameHistoryEntry ToEntry(
                DateTimeOffset endedAt,
                GameHistoryStats finalStats,
                TimeSpan? gameDuration)
            {
                return new GameHistoryEntry
                {
                    StartedAt = StartedAt,
                    EndedAt = endedAt,
                    MapIdentity = GameHistoryMapIdentity.FromGameMapIdentity(MapIdentity),
                    FinalRound = Rounds[^1].RoundNumber,
                    FinalStats = finalStats,
                    GameDuration = gameDuration,
                    Rounds = [.. Rounds],
                    BoxEvents = [.. BoxEvents]
                };
            }
        }

        private sealed class ActiveRound(int roundNumber, DateTimeOffset startedAt)
        {
            public int RoundNumber { get; } = roundNumber;

            public DateTimeOffset StartedAt { get; } = startedAt;

            public GameHistoryStats? BaselineStats { get; set; }
        }
    }
}
