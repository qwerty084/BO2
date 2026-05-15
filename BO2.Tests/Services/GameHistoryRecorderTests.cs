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
        public void ObserveSnapshot_WhenFarmGameCompletes_SavesSummaryRoundsDurationsAndBoxEvents()
        {
            List<GameHistoryEntry> savedEntries = [];
            var recorder = new GameHistoryRecorder(savedEntries.Add);
            DetectedGame detectedGame = CreateGame();
            DateTimeOffset startedAt = new(2026, 5, 14, 13, 44, 0, TimeSpan.Zero);

            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(10, 0, 0, 0, 0),
                CreateTimers(gameSeconds: 0, roundSeconds: 0),
                CreateCompatibleStatus(CreateEvent(GameEventType.StartOfRound, 1, startedAt, sequence: 1)),
                CreateFarmResult(detectedGame)));
            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(260, 4, 0, 0, 1),
                CreateTimers(gameSeconds: 25, roundSeconds: 25),
                CreateCompatibleStatus(CreateBoxEvent(startedAt.AddSeconds(25), sequence: 2, "saiga12_zm", ownerId: 9)),
                CreateFarmResult(detectedGame)));
            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(500, 7, 0, 0, 2),
                CreateTimers(gameSeconds: 60, roundSeconds: 60),
                CreateCompatibleStatus(CreateEvent(GameEventType.EndGame, 1, startedAt.AddSeconds(60), sequence: 3)),
                CreateFarmResult(detectedGame)));

            GameHistoryEntry saved = Assert.Single(savedEntries);
            Assert.Equal("zm_transit", saved.MapIdentity.BaseMapToken);
            Assert.Equal("farm", saved.MapIdentity.StartLocationToken);
            Assert.Equal("zm_transit_gump_farm", saved.MapIdentity.InternalMapToken);
            Assert.Equal("Farm", saved.MapIdentity.FriendlyName);
            Assert.Equal(1, saved.FinalRound);
            Assert.Equal(500, saved.FinalStats.Points);
            Assert.Equal(TimeSpan.FromSeconds(60), saved.GameDuration);
            GameHistoryRound round = Assert.Single(saved.Rounds);
            Assert.Equal(1, round.RoundNumber);
            Assert.Equal(490, round.DeltaStats.Points);
            Assert.Equal(7, round.DeltaStats.Kills);
            Assert.Equal(TimeSpan.FromSeconds(60), round.RoundDuration);
            GameHistoryBoxEvent boxEvent = Assert.Single(saved.BoxEvents);
            Assert.Equal("saiga12_zm", boxEvent.RawWeaponToken);
            Assert.Equal("S12", boxEvent.WeaponDisplayName);
            Assert.Equal(GameHistoryRecordingState.Saved, recorder.Status.State);
            Assert.Equal("Farm", recorder.Status.MapName);
        }

        [Fact]
        public void ObserveSnapshot_WhenBuriedGameCompletes_SavesStandaloneMapIdentity()
        {
            List<GameHistoryEntry> savedEntries = [];
            var recorder = new GameHistoryRecorder(savedEntries.Add);
            DetectedGame detectedGame = CreateGame();
            DateTimeOffset startedAt = new(2026, 5, 15, 15, 23, 0, TimeSpan.Zero);
            GameMapIdentityReadResult mapIdentityResult = CreateBuriedResult(detectedGame);

            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(0, 0, 0, 0, 0),
                CreateTimers(gameSeconds: 0, roundSeconds: 0),
                CreateCompatibleStatus(CreateEvent(GameEventType.StartOfRound, 1, startedAt, sequence: 1)),
                mapIdentityResult));
            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(720, 13, 0, 0, 6),
                CreateTimers(gameSeconds: 143, roundSeconds: 80),
                CreateCompatibleStatus(CreateBoxEvent(startedAt.AddSeconds(80), sequence: 2, "tar21_zm", ownerId: 11)),
                mapIdentityResult));
            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(720, 13, 0, 0, 6),
                CreateTimers(gameSeconds: 145, roundSeconds: 82),
                CreateCompatibleStatus(CreateEvent(GameEventType.EndGame, 1, startedAt.AddSeconds(145), sequence: 3)),
                mapIdentityResult));

            GameHistoryEntry saved = Assert.Single(savedEntries);
            Assert.Equal("zm_buried", saved.MapIdentity.BaseMapToken);
            Assert.Null(saved.MapIdentity.StartLocationToken);
            Assert.Equal("zm_buried", saved.MapIdentity.InternalMapToken);
            Assert.Equal("Buried", saved.MapIdentity.FriendlyName);
            Assert.Equal(1, saved.FinalRound);
            Assert.Equal(720, saved.FinalStats.Points);
            Assert.Equal(TimeSpan.FromSeconds(145), saved.GameDuration);
            GameHistoryBoxEvent boxEvent = Assert.Single(saved.BoxEvents);
            Assert.Equal("tar21_zm", boxEvent.RawWeaponToken);
            Assert.Equal("MTAR", boxEvent.WeaponDisplayName);
            Assert.Equal(GameHistoryRecordingState.Saved, recorder.Status.State);
            Assert.Equal("Buried", recorder.Status.MapName);
        }

        [Fact]
        public void ObserveSnapshot_WhenDieRiseGameCompletes_SavesStandaloneMapIdentity()
        {
            List<GameHistoryEntry> savedEntries = [];
            var recorder = new GameHistoryRecorder(savedEntries.Add);
            DetectedGame detectedGame = CreateGame();
            DateTimeOffset startedAt = new(2026, 5, 15, 16, 8, 0, TimeSpan.Zero);
            GameMapIdentityReadResult mapIdentityResult = CreateDieRiseResult(detectedGame);

            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(0, 0, 0, 0, 0),
                CreateTimers(gameSeconds: 0, roundSeconds: 0),
                CreateCompatibleStatus(CreateEvent(GameEventType.StartOfRound, 1, startedAt, sequence: 1)),
                mapIdentityResult));
            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(1080, 14, 0, 0, 5),
                CreateTimers(gameSeconds: 180, roundSeconds: 180),
                CreateCompatibleStatus(CreateEvent(GameEventType.EndGame, 1, startedAt.AddSeconds(180), sequence: 2)),
                mapIdentityResult));

            GameHistoryEntry saved = Assert.Single(savedEntries);
            Assert.Equal("zm_highrise", saved.MapIdentity.BaseMapToken);
            Assert.Null(saved.MapIdentity.StartLocationToken);
            Assert.Equal("zm_highrise", saved.MapIdentity.InternalMapToken);
            Assert.Equal("Die Rise", saved.MapIdentity.FriendlyName);
            Assert.Equal(1, saved.FinalRound);
            Assert.Equal(1080, saved.FinalStats.Points);
            Assert.Equal(TimeSpan.FromSeconds(180), saved.GameDuration);
            Assert.Equal(GameHistoryRecordingState.Saved, recorder.Status.State);
            Assert.Equal("Die Rise", recorder.Status.MapName);
        }

        [Theory]
        [InlineData("zm_transit_gump_transit_zclassic", "TranZit")]
        [InlineData("zm_transit_gump_transit_zstandard", "Bus Depot")]
        public void ObserveSnapshot_WhenRemainingGreenRunGameCompletes_SavesMapIdentity(
            string internalMapToken,
            string friendlyName)
        {
            List<GameHistoryEntry> savedEntries = [];
            var recorder = new GameHistoryRecorder(savedEntries.Add);
            DetectedGame detectedGame = CreateGame();
            DateTimeOffset startedAt = new(2026, 5, 15, 14, 30, 0, TimeSpan.Zero);
            GameMapIdentityReadResult mapIdentityResult = CreateGreenRunTransitResult(
                detectedGame,
                internalMapToken,
                friendlyName);

            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(0, 0, 0, 0, 0),
                CreateTimers(gameSeconds: 0, roundSeconds: 0),
                CreateCompatibleStatus(CreateEvent(GameEventType.StartOfRound, 1, startedAt, sequence: 1)),
                mapIdentityResult));
            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(500, 7, 0, 0, 2),
                CreateTimers(gameSeconds: 60, roundSeconds: 60),
                CreateCompatibleStatus(CreateEvent(GameEventType.EndGame, 1, startedAt.AddSeconds(60), sequence: 2)),
                mapIdentityResult));

            GameHistoryEntry saved = Assert.Single(savedEntries);
            Assert.Equal("zm_transit", saved.MapIdentity.BaseMapToken);
            Assert.Equal("transit", saved.MapIdentity.StartLocationToken);
            Assert.Equal(internalMapToken, saved.MapIdentity.InternalMapToken);
            Assert.Equal(friendlyName, saved.MapIdentity.FriendlyName);
            Assert.Equal(GameHistoryRecordingState.Saved, recorder.Status.State);
            Assert.Equal(friendlyName, recorder.Status.MapName);
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
        [InlineData(GameHistoryRecordingDiscardReason.MissingMapIdentity)]
        [InlineData(GameHistoryRecordingDiscardReason.UnsupportedMapIdentity)]
        [InlineData(GameHistoryRecordingDiscardReason.MissingFriendlyMapName)]
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
        public void DiscardForAppClose_WhenCandidateActive_DiscardsWithoutSaving()
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

            recorder.DiscardForAppClose();

            Assert.Empty(savedEntries);
            Assert.Equal(GameHistoryRecordingState.Discarded, recorder.Status.State);
            Assert.Equal(GameHistoryRecordingDiscardReason.AppClosed, recorder.Status.DiscardReason);
        }

        [Theory]
        [InlineData(GameHistoryRecordingUnavailableReason.MissingMapIdentity)]
        [InlineData(GameHistoryRecordingUnavailableReason.RequiresSupportedMap)]
        [InlineData(GameHistoryRecordingUnavailableReason.MissingFriendlyMapName)]
        public void ObserveSnapshot_WhenMapIdentityUnavailableBeforeRoundOne_BlocksCandidate(
            GameHistoryRecordingUnavailableReason reason)
        {
            List<GameHistoryEntry> savedEntries = [];
            var recorder = new GameHistoryRecorder(savedEntries.Add);
            DetectedGame detectedGame = CreateGame();
            DateTimeOffset startedAt = new(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);

            recorder.ObserveSnapshot(CreateSnapshot(
                detectedGame,
                CreateStats(0, 0, 0, 0, 0),
                CreateTimers(0, 0),
                CreateCompatibleStatus(CreateEvent(GameEventType.StartOfRound, 1, startedAt, sequence: 1)),
                CreateUnavailableMapIdentityResult(reason, detectedGame)));

            Assert.Empty(savedEntries);
            Assert.Equal(GameHistoryRecordingState.Unavailable, recorder.Status.State);
            Assert.Equal(reason, recorder.Status.UnavailableReason);
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
                GameHistoryRecordingDiscardReason.MissingMapIdentity => CreateSnapshot(
                    detectedGame,
                    CreateStats(0, 0, 0, 0, 0),
                    CreateTimers(1, 1),
                    CreateCompatibleStatus(),
                    mapIdentityResult: GameMapIdentityReadResult.MissingMapIdentity(detectedGame)),
                GameHistoryRecordingDiscardReason.UnsupportedMapIdentity => CreateSnapshot(
                    detectedGame,
                    CreateStats(0, 0, 0, 0, 0),
                    CreateTimers(1, 1),
                    CreateCompatibleStatus(),
                    mapIdentityResult: GameMapIdentityReadResult.UnsupportedMapIdentity(detectedGame)),
                GameHistoryRecordingDiscardReason.MissingFriendlyMapName => CreateSnapshot(
                    detectedGame,
                    CreateStats(0, 0, 0, 0, 0),
                    CreateTimers(1, 1),
                    CreateCompatibleStatus(),
                    mapIdentityResult: CreateBlankFriendlyMapResult(detectedGame)),
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
            return GameMapIdentityReadResult.SupportedMap(
                detectedGame,
                new GameMapIdentity("zm_transit", "town", "zm_transit_gump_town", "Town"));
        }

        private static GameMapIdentityReadResult CreateFarmResult(DetectedGame detectedGame)
        {
            return GameMapIdentityReadResult.SupportedMap(
                detectedGame,
                new GameMapIdentity("zm_transit", "farm", "zm_transit_gump_farm", "Farm"));
        }

        private static GameMapIdentityReadResult CreateGreenRunTransitResult(
            DetectedGame detectedGame,
            string internalMapToken,
            string friendlyName)
        {
            return GameMapIdentityReadResult.SupportedMap(
                detectedGame,
                new GameMapIdentity("zm_transit", "transit", internalMapToken, friendlyName));
        }

        private static GameMapIdentityReadResult CreateBuriedResult(DetectedGame detectedGame)
        {
            return GameMapIdentityReadResult.SupportedMap(
                detectedGame,
                new GameMapIdentity("zm_buried", null, "zm_buried", "Buried"));
        }

        private static GameMapIdentityReadResult CreateDieRiseResult(DetectedGame detectedGame)
        {
            return GameMapIdentityReadResult.SupportedMap(
                detectedGame,
                new GameMapIdentity("zm_highrise", null, "zm_highrise", "Die Rise"));
        }

        private static GameMapIdentityReadResult CreateBlankFriendlyMapResult(DetectedGame detectedGame)
        {
            return GameMapIdentityReadResult.SupportedMap(
                detectedGame,
                new GameMapIdentity("zm_transit", "farm", "zm_transit_gump_farm", " "));
        }

        private static GameMapIdentityReadResult CreateUnavailableMapIdentityResult(
            GameHistoryRecordingUnavailableReason reason,
            DetectedGame detectedGame)
        {
            return reason switch
            {
                GameHistoryRecordingUnavailableReason.MissingMapIdentity =>
                    GameMapIdentityReadResult.MissingMapIdentity(detectedGame),
                GameHistoryRecordingUnavailableReason.RequiresSupportedMap =>
                    GameMapIdentityReadResult.UnsupportedMapIdentity(detectedGame),
                GameHistoryRecordingUnavailableReason.MissingFriendlyMapName =>
                    CreateBlankFriendlyMapResult(detectedGame),
                _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
            };
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
