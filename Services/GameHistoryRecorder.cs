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
        Saved
    }

    public enum GameHistoryRecordingStatusKind
    {
        WaitingForConnection,
        ActiveRecording,
        RequiresTown,
        RequiresHookBackedEventMonitor,
        DiscardedSequenceOrDroppedLifecycle,
        DiscardedMissingStats,
        DiscardedConnectionEndedBeforeSave
    }

    public enum GameHistoryRecordingUnavailableReason
    {
        None,
        NotConnected,
        RequiresTown,
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
        string? MapName = null)
    {
        public GameHistoryRecordingStatusKind Kind => State switch
        {
            GameHistoryRecordingState.Recording => GameHistoryRecordingStatusKind.ActiveRecording,
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
                GameHistoryRecordingUnavailableReason.RequiresTown
                    or GameHistoryRecordingUnavailableReason.MissingMapIdentity
                    or GameHistoryRecordingUnavailableReason.MissingFriendlyMapName => GameHistoryRecordingStatusKind.RequiresTown,
                _ => GameHistoryRecordingStatusKind.WaitingForConnection
            },
            _ => GameHistoryRecordingStatusKind.WaitingForConnection
        };

        public static GameHistoryRecordingStatus WaitingForConnection { get; } =
            Unavailable(GameHistoryRecordingUnavailableReason.NotConnected);

        public static GameHistoryRecordingStatus RequiresTown { get; } =
            Unavailable(GameHistoryRecordingUnavailableReason.RequiresTown);

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

        public static GameHistoryRecordingStatus WaitingForRoundOne { get; } = new(
            GameHistoryRecordingState.WaitingForRoundOne,
            GameHistoryRecordingUnavailableReason.None,
            GameHistoryRecordingDiscardReason.None,
            null,
            null,
            null);

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

        public static GameHistoryRecordingStatus Saved(string historyId)
        {
            return new GameHistoryRecordingStatus(
                GameHistoryRecordingState.Saved,
                GameHistoryRecordingUnavailableReason.None,
                GameHistoryRecordingDiscardReason.None,
                null,
                historyId,
                null);
        }
    }

    internal sealed class GameHistoryRecorder
    {
        private readonly Action<GameHistoryEntry> _saveEntry;
        private CandidateSession? _candidate;
        private DetectedGame? _observedGame;
        private ulong? _lastObservedEventSequence;
        private uint _lastDroppedEventCount;
        private uint _lastDroppedNotifyCount;

        public GameHistoryRecorder(GameHistoryStore store)
            : this(store.Append)
        {
        }

        internal GameHistoryRecorder(Action<GameHistoryEntry> saveEntry)
        {
            ArgumentNullException.ThrowIfNull(saveEntry);

            _saveEntry = saveEntry;
            Status = GameHistoryRecordingStatus.Unavailable(GameHistoryRecordingUnavailableReason.NotConnected);
        }

        public GameHistoryRecordingStatus Status { get; private set; }

        public void ObserveSnapshot(GameConnectionSnapshot snapshot)
        {
            if (HasDetectedGameChanged(snapshot.CurrentGame))
            {
                ResetEventTracking();
                if (_candidate is not null)
                {
                    Discard(GameHistoryRecordingDiscardReason.DetectedGameChanged);
                    _observedGame = snapshot.CurrentGame;
                    return;
                }
            }

            _observedGame = snapshot.CurrentGame;

            if (snapshot.ConnectionPhase != GameConnectionPhase.Connected)
            {
                ResetEventTracking();
                if (_candidate is not null)
                {
                    Discard(GameHistoryRecordingDiscardReason.Disconnected);
                    return;
                }

                Status = GameHistoryRecordingStatus.Unavailable(GameHistoryRecordingUnavailableReason.NotConnected);
                return;
            }

            GameConnectionEventMonitorSummary eventMonitorSummary = snapshot.EventMonitorSummary;
            if (eventMonitorSummary.State == GameConnectionEventMonitorState.PollingFallback)
            {
                ResetEventTracking();
                if (_candidate is not null)
                {
                    Discard(GameHistoryRecordingDiscardReason.PollingFallback);
                    return;
                }

                Status = GameHistoryRecordingStatus.Unavailable(
                    GameHistoryRecordingUnavailableReason.RequiresHookBackedEventMonitor);
                return;
            }

            if (eventMonitorSummary.State != GameConnectionEventMonitorState.Ready
                || eventMonitorSummary.Status.CompatibilityState != GameCompatibilityState.Compatible)
            {
                if (_candidate is not null)
                {
                    Discard(GameHistoryRecordingDiscardReason.DroppedLifecycleData);
                    return;
                }

                Status = GameHistoryRecordingStatus.Unavailable(
                    GameHistoryRecordingUnavailableReason.RequiresHookBackedEventMonitor);
                return;
            }

            if (!TryGetConfirmedTown(
                snapshot.MapIdentityResult,
                out GameMapIdentity? mapIdentity,
                out GameHistoryRecordingUnavailableReason unavailableReason,
                out GameHistoryRecordingDiscardReason discardReason))
            {
                if (_candidate is not null)
                {
                    Discard(discardReason);
                    return;
                }

                Status = GameHistoryRecordingStatus.Unavailable(unavailableReason);
                return;
            }

            GameHistoryEventBatch eventBatch = ReadNewEvents(eventMonitorSummary.Status);
            if (HasDroppedLifecycleData(eventMonitorSummary.Status))
            {
                if (_candidate is not null)
                {
                    Discard(GameHistoryRecordingDiscardReason.DroppedLifecycleData);
                    return;
                }

                Status = GameHistoryRecordingStatus.Unavailable(
                    GameHistoryRecordingUnavailableReason.RequiresHookBackedEventMonitor);
                return;
            }

            if (eventBatch.HasSequenceGap)
            {
                if (_candidate is not null)
                {
                    Discard(GameHistoryRecordingDiscardReason.SequenceGap);
                    return;
                }

                Status = GameHistoryRecordingStatus.Unavailable(
                    GameHistoryRecordingUnavailableReason.RequiresHookBackedEventMonitor);
                return;
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
                ProcessEvent(snapshot, gameEvent, mapIdentity, observedStats, gameDuration, roundDuration);
                if (Status.State == GameHistoryRecordingState.Discarded)
                {
                    return;
                }
            }

            if (_candidate is not null)
            {
                Status = GameHistoryRecordingStatus.Recording(
                    _candidate.ActiveRound?.RoundNumber ?? 0,
                    _candidate.MapIdentity.DisplayName);
            }
            else if (Status.State != GameHistoryRecordingState.Saved)
            {
                Status = GameHistoryRecordingStatus.WaitingForRoundOne;
            }
        }

        public void DiscardForAppClose()
        {
            if (_candidate is not null)
            {
                Discard(GameHistoryRecordingDiscardReason.AppClosed);
            }
        }

        private void ProcessEvent(
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
                    HandleEndGame(gameEvent, observedStats, gameDuration, roundDuration);
                    break;
                case GameEventType.BoxEvent:
                    HandleBoxEvent(gameEvent);
                    break;
            }
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

        private void HandleEndGame(
            GameEvent gameEvent,
            GameHistoryStats? observedStats,
            TimeSpan? gameDuration,
            TimeSpan? roundDuration)
        {
            if (_candidate is null)
            {
                return;
            }

            GameHistoryStats? finalStats = observedStats ?? _candidate.LatestStats;
            if (_candidate.ActiveRound is not null
                && !TryCloseActiveRound(gameEvent.ReceivedAt, finalStats, roundDuration))
            {
                Discard(GameHistoryRecordingDiscardReason.MissingRequiredStats);
                return;
            }

            finalStats ??= _candidate.LatestStats;
            if (finalStats is null || _candidate.Rounds.Count == 0)
            {
                Discard(GameHistoryRecordingDiscardReason.MissingRequiredStats);
                return;
            }

            GameHistoryEntry entry = _candidate.ToEntry(
                gameEvent.ReceivedAt,
                finalStats,
                gameDuration ?? _candidate.LatestGameDuration);
            _saveEntry(entry);
            _candidate = null;
            Status = GameHistoryRecordingStatus.Saved(entry.Id);
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

        private static bool TryGetConfirmedTown(
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

            if (mapIdentityResult.Status == GameMapIdentityReadStatus.ConfirmedTown
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
                    unavailableReason = GameHistoryRecordingUnavailableReason.RequiresTown;
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
