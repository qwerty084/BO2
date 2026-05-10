using System;
using System.Collections.Generic;
using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameHistoryRecorderTests
    {
        [Fact]
        public void ObserveSnapshot_WhenTownGameCompletes_SavesSummaryRoundsDeltasAndBoxEvents()
        {
            List<GameHistoryEntry> savedEntries = [];
            var recorder = new GameHistoryRecorder(savedEntries.Add);
            DetectedGame detectedGame = CreateGame();
            DateTimeOffset startedAt = new(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);

            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(100, 1, 0, 0, 0),
                CreateTimers(gameSeconds: 0, roundSeconds: 0),
                CreateCompatibleStatus(CreateEvent(GameEventType.StartOfRound, 1, startedAt, sequence: 1))));
            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(250, 3, 0, 0, 1),
                CreateTimers(gameSeconds: 10, roundSeconds: 10),
                CreateCompatibleStatus(CreateBoxEvent(startedAt.AddSeconds(10), sequence: 2, "ray_gun_zm", ownerId: 7))));
            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(500, 7, 0, 0, 3),
                CreateTimers(gameSeconds: 45, roundSeconds: 45),
                CreateCompatibleStatus(CreateEvent(GameEventType.EndOfRound, 1, startedAt.AddSeconds(45), sequence: 3))));
            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(500, 7, 0, 0, 3),
                CreateTimers(gameSeconds: 50, roundSeconds: 0),
                CreateCompatibleStatus(CreateEvent(GameEventType.StartOfRound, 2, startedAt.AddSeconds(50), sequence: 4))));
            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(750, 10, 0, 0, 4),
                CreateTimers(gameSeconds: 70, roundSeconds: 20),
                CreateCompatibleStatus(CreateBoxEvent(startedAt.AddSeconds(70), sequence: 5, "zm_future", ownerId: 42))));
            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(1200, 16, 1, 0, 8),
                CreateTimers(gameSeconds: 95, roundSeconds: 45),
                CreateCompatibleStatus(CreateEvent(GameEventType.EndGame, 2, startedAt.AddSeconds(95), sequence: 6))));

            GameHistoryEntry saved = Assert.Single(savedEntries);
            Assert.Equal(startedAt, saved.StartedAt);
            Assert.Equal(startedAt.AddSeconds(95), saved.EndedAt);
            Assert.Equal("Town", saved.MapIdentity.FriendlyName);
            Assert.Equal(2, saved.FinalRound);
            Assert.Equal(1200, saved.FinalStats.Points);
            Assert.Equal(TimeSpan.FromSeconds(95), saved.GameDuration);
            Assert.Equal(2, saved.Rounds.Count);
            Assert.Equal(1, saved.Rounds[0].RoundNumber);
            Assert.Equal(400, saved.Rounds[0].DeltaStats.Points);
            Assert.Equal(6, saved.Rounds[0].DeltaStats.Kills);
            Assert.Equal(TimeSpan.FromSeconds(45), saved.Rounds[0].RoundDuration);
            Assert.Equal(2, saved.Rounds[1].RoundNumber);
            Assert.Equal(700, saved.Rounds[1].DeltaStats.Points);
            Assert.Equal(9, saved.Rounds[1].DeltaStats.Kills);
            Assert.Equal(TimeSpan.FromSeconds(45), saved.Rounds[1].RoundDuration);
            Assert.Equal(2, saved.BoxEvents.Count);
            Assert.Equal(1, saved.BoxEvents[0].RoundNumber);
            Assert.Equal("ray_gun_zm", saved.BoxEvents[0].RawWeaponToken);
            Assert.Equal("Ray Gun", saved.BoxEvents[0].WeaponDisplayName);
            Assert.Equal((uint)7, saved.BoxEvents[0].OwnerId);
            Assert.Equal(2, saved.BoxEvents[1].RoundNumber);
            Assert.Equal("zm_future", saved.BoxEvents[1].RawWeaponToken);
            Assert.Null(saved.BoxEvents[1].WeaponDisplayName);
            Assert.Equal(GameHistoryRecordingState.Saved, recorder.Status.State);
            Assert.Equal(saved.Id, recorder.Status.LastSavedHistoryId);
        }

        [Fact]
        public void ObserveSnapshot_IgnoresEventsBeforeRoundOneAndAfterGameOver()
        {
            List<GameHistoryEntry> savedEntries = [];
            var recorder = new GameHistoryRecorder(savedEntries.Add);
            DetectedGame detectedGame = CreateGame();
            DateTimeOffset startedAt = new(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);

            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(0, 0, 0, 0, 0),
                CreateTimers(0, 0),
                CreateCompatibleStatus(CreateBoxEvent(startedAt.AddSeconds(-5), sequence: 1, "ray_gun_zm", ownerId: 7))));
            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(0, 0, 0, 0, 0),
                CreateTimers(0, 0),
                CreateCompatibleStatus(CreateEvent(GameEventType.StartOfRound, 1, startedAt, sequence: 2))));
            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(100, 2, 0, 0, 1),
                CreateTimers(30, 30),
                CreateCompatibleStatus(CreateEvent(GameEventType.EndGame, 1, startedAt.AddSeconds(30), sequence: 3))));
            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(100, 2, 0, 0, 1),
                CreateTimers(30, 30),
                CreateCompatibleStatus(CreateBoxEvent(startedAt.AddSeconds(31), sequence: 4, "python_zm", ownerId: 8))));

            GameHistoryEntry saved = Assert.Single(savedEntries);
            Assert.Empty(saved.BoxEvents);
        }

        [Theory]
        [InlineData(GameHistoryRecordingDiscardReason.PollingFallback)]
        [InlineData(GameHistoryRecordingDiscardReason.SequenceGap)]
        [InlineData(GameHistoryRecordingDiscardReason.DroppedLifecycleData)]
        [InlineData(GameHistoryRecordingDiscardReason.MissingRequiredStats)]
        [InlineData(GameHistoryRecordingDiscardReason.UnsupportedMapIdentity)]
        [InlineData(GameHistoryRecordingDiscardReason.Disconnected)]
        [InlineData(GameHistoryRecordingDiscardReason.DetectedGameChanged)]
        public void ObserveSnapshot_WhenCandidateBecomesUntrusted_DiscardsWithoutSaving(
            GameHistoryRecordingDiscardReason reason)
        {
            List<GameHistoryEntry> savedEntries = [];
            var recorder = new GameHistoryRecorder(savedEntries.Add);
            DetectedGame detectedGame = CreateGame();
            DateTimeOffset startedAt = new(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);
            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(0, 0, 0, 0, 0),
                CreateTimers(0, 0),
                CreateCompatibleStatus(CreateEvent(GameEventType.StartOfRound, 1, startedAt, sequence: 1))));

            recorder.ObserveSnapshot(CreateUntrustedSnapshot(reason, detectedGame, startedAt));

            Assert.Empty(savedEntries);
            Assert.Equal(GameHistoryRecordingState.Discarded, recorder.Status.State);
            Assert.Equal(reason, recorder.Status.DiscardReason);
        }

        [Fact]
        public void ObserveSnapshot_RecordsMultipleGamesWithinOneConnection()
        {
            List<GameHistoryEntry> savedEntries = [];
            var recorder = new GameHistoryRecorder(savedEntries.Add);
            DetectedGame detectedGame = CreateGame();
            DateTimeOffset firstStart = new(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);
            DateTimeOffset secondStart = firstStart.AddMinutes(10);

            CompleteOneRoundGame(recorder, detectedGame, firstStart, firstSequence: 1, finalPoints: 100);
            CompleteOneRoundGame(recorder, detectedGame, secondStart, firstSequence: 3, finalPoints: 250);

            Assert.Equal(2, savedEntries.Count);
            Assert.Equal(100, savedEntries[0].FinalStats.Points);
            Assert.Equal(250, savedEntries[1].FinalStats.Points);
        }

        private static void CompleteOneRoundGame(
            GameHistoryRecorder recorder,
            DetectedGame detectedGame,
            DateTimeOffset startedAt,
            ulong firstSequence,
            int finalPoints)
        {
            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(0, 0, 0, 0, 0),
                CreateTimers(0, 0),
                CreateCompatibleStatus(CreateEvent(GameEventType.StartOfRound, 1, startedAt, firstSequence))));
            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(finalPoints, 3, 0, 0, 1),
                CreateTimers(40, 40),
                CreateCompatibleStatus(CreateEvent(GameEventType.EndGame, 1, startedAt.AddSeconds(40), firstSequence + 1))));
        }

        private static GameConnectionSnapshot CreateUntrustedSnapshot(
            GameHistoryRecordingDiscardReason reason,
            DetectedGame detectedGame,
            DateTimeOffset startedAt)
        {
            return reason switch
            {
                GameHistoryRecordingDiscardReason.PollingFallback => CreateSnapshot(
                    detectedGame,
                    CreateStats(0, 0, 0, 0, 0),
                    CreateTimers(1, 1),
                    new GameConnectionEventMonitorSummary(
                        GameConnectionEventMonitorState.PollingFallback,
                        new GameEventMonitorStatus(GameCompatibilityState.PollingFallback, 0, 0, 0, []))),
                GameHistoryRecordingDiscardReason.SequenceGap => CreateSnapshot(
                    detectedGame,
                    CreateStats(0, 0, 0, 0, 0),
                    CreateTimers(1, 1),
                    CreateCompatibleStatus(CreateEvent(GameEventType.EndGame, 1, startedAt.AddSeconds(1), sequence: 3))),
                GameHistoryRecordingDiscardReason.DroppedLifecycleData => CreateSnapshot(
                    detectedGame,
                    CreateStats(0, 0, 0, 0, 0),
                    CreateTimers(1, 1),
                    new GameConnectionEventMonitorSummary(
                        GameConnectionEventMonitorState.Ready,
                        new GameEventMonitorStatus(GameCompatibilityState.Compatible, 1, 0, 1, []))),
                GameHistoryRecordingDiscardReason.MissingRequiredStats => CreateSnapshot(
                    detectedGame,
                    null,
                    CreateTimers(1, 1),
                    CreateCompatibleStatus(CreateEvent(GameEventType.EndOfRound, 1, startedAt.AddSeconds(1), sequence: 2))),
                GameHistoryRecordingDiscardReason.UnsupportedMapIdentity => CreateSnapshot(
                    detectedGame,
                    CreateStats(0, 0, 0, 0, 0),
                    CreateTimers(1, 1),
                    CreateCompatibleStatus(),
                    mapIdentityResult: GameMapIdentityReadResult.UnsupportedMapIdentity(detectedGame)),
                GameHistoryRecordingDiscardReason.Disconnected => CreateSnapshot(
                    detectedGame,
                    CreateStats(0, 0, 0, 0, 0),
                    CreateTimers(1, 1),
                    CreateCompatibleStatus(),
                    connectionPhase: GameConnectionPhase.Detected),
                GameHistoryRecordingDiscardReason.DetectedGameChanged => CreateSnapshot(
                    CreateGame(processId: 2002),
                    CreateStats(0, 0, 0, 0, 0),
                    CreateTimers(1, 1),
                    CreateCompatibleStatus()),
                _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
            };
        }

        private static GameConnectionSnapshot CreateSnapshot(
            DetectedGame detectedGame,
            PlayerStatsReadResult? readResult,
            GameConnectionTimerDisplayState timers,
            GameConnectionEventMonitorSummary eventMonitorSummary,
            GameMapIdentityReadResult? mapIdentityResult = null,
            GameConnectionPhase connectionPhase = GameConnectionPhase.Connected)
        {
            return new GameConnectionSnapshot(
                detectedGame,
                connectionPhase,
                readResult,
                eventMonitorSummary,
                timers,
                GameConnectionCommandAvailability.Hidden,
                GameConnectionCommandAvailability.VisibleEnabled,
                mapIdentityResult ?? CreateTownResult(detectedGame));
        }

        private static GameConnectionSnapshot CreateSnapshot(
            DetectedGame detectedGame,
            PlayerStatsReadResult? readResult,
            GameConnectionTimerDisplayState timers,
            GameEventMonitorStatus eventStatus,
            GameMapIdentityReadResult? mapIdentityResult = null,
            GameConnectionPhase connectionPhase = GameConnectionPhase.Connected)
        {
            return CreateSnapshot(
                detectedGame,
                readResult,
                timers,
                new GameConnectionEventMonitorSummary(GameConnectionEventMonitorState.Ready, eventStatus),
                mapIdentityResult,
                connectionPhase);
        }

        private static GameEventMonitorStatus CreateCompatibleStatus(params GameEvent[] events)
        {
            return new GameEventMonitorStatus(GameCompatibilityState.Compatible, 0, 0, 1, events);
        }

        private static GameEvent CreateEvent(
            GameEventType eventType,
            int round,
            DateTimeOffset receivedAt,
            ulong sequence)
        {
            return new GameEvent(eventType, eventType.ToString(), round, 0, 0, receivedAt, Sequence: sequence);
        }

        private static GameEvent CreateBoxEvent(
            DateTimeOffset receivedAt,
            ulong sequence,
            string weaponToken,
            uint ownerId)
        {
            return new GameEvent(
                GameEventType.BoxEvent,
                "randomization_done",
                0,
                ownerId,
                100,
                receivedAt,
                weaponToken,
                sequence);
        }

        private static PlayerStatsReadResult CreateStats(
            int points,
            int kills,
            int downs,
            int revives,
            int headshots)
        {
            DetectedGame detectedGame = CreateGame();
            return new PlayerStatsReadResult(
                detectedGame,
                new PlayerStats(
                    points,
                    kills,
                    downs,
                    revives,
                    headshots,
                    new PlayerCandidateStats(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null)));
        }

        private static GameConnectionTimerDisplayState CreateTimers(int gameSeconds, int roundSeconds)
        {
            return new GameConnectionTimerDisplayState(
                TimerDisplayState.Active(TimeSpan.FromSeconds(gameSeconds)),
                TimerDisplayState.Active(TimeSpan.FromSeconds(roundSeconds)));
        }

        private static GameMapIdentityReadResult CreateTownResult(DetectedGame detectedGame)
        {
            return GameMapIdentityReadResult.ConfirmedTown(
                detectedGame,
                new GameMapIdentity("zm_transit", "town", "zm_transit_gump_town", "Town"));
        }

        private static DetectedGame CreateGame(int processId = 1001)
        {
            return new DetectedGame(
                GameVariant.SteamZombies,
                "Steam Zombies",
                "t6zm",
                processId,
                PlayerStatAddressMap.SteamZombies,
                null);
        }
    }
}
