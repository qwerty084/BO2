using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class CurrentGamePageDisplayProjectorTests
    {
        [Fact]
        public void Project_WhenNoGame_ReturnsEmptyCurrentGameState()
        {
            CurrentGamePageDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    null,
                    null,
                    connectionPhase: GameConnectionPhase.NoGame));

            Assert.Equal("CurrentGamePageStatusNotConnected", state.PageStatusText);
            AssertInactiveStats(state);
            AssertInactiveEvents(state);
        }

        [Fact]
        public void Project_WhenUnsupportedGame_ReturnsUnsupportedCurrentGameState()
        {
            DetectedGame detectedGame = CreateUnsupportedGame(1001);

            CurrentGamePageDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    null,
                    connectionPhase: GameConnectionPhase.UnsupportedGame));

            Assert.Equal("CurrentGamePageStatusNotConnected", state.PageStatusText);
            AssertInactiveStats(state);
            AssertInactiveEvents(state);
        }

        [Fact]
        public void Project_WhenDetectedWithoutStats_ReturnsDetectedCurrentGameState()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);

            CurrentGamePageDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    null,
                    canAttemptConnect: true,
                    connectionPhase: GameConnectionPhase.Detected));

            Assert.Equal("CurrentGamePageStatusNotConnected", state.PageStatusText);
            AssertInactiveStats(state);
            AssertInactiveEvents(state);
        }

        [Fact]
        public void Project_WhenDetectedAfterDisconnect_ReturnsInactiveStateWithoutStaleEvents()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);
            DateTimeOffset receivedAt = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
            GameEventMonitorStatus staleEventStatus = new(
                GameCompatibilityState.Compatible,
                DroppedEventCount: 0,
                DroppedNotifyCount: 0,
                PublishedNotifyCount: 1,
                RecentEvents:
                [
                    new GameEvent(GameEventType.StartOfRound, "start_of_round", 5, 0, 0, receivedAt),
                    new GameEvent(GameEventType.BoxEvent, "randomization_done", 5, 10, 20, receivedAt, "ray_gun_zm")
                ]);

            CurrentGamePageDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    null,
                    connectionPhase: GameConnectionPhase.Detected,
                    canAttemptConnect: true,
                    eventMonitorSummary: GameConnectionEventMonitorSummary.FromStatus(staleEventStatus)));

            Assert.Equal("CurrentGamePageStatusNotConnected", state.PageStatusText);
            AssertInactiveStats(state);
            AssertInactiveEvents(state);
        }

        [Fact]
        public void Project_WhenEventMonitorReadinessFailedBeforeConnect_ReturnsInactiveState()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);

            CurrentGamePageDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    null,
                    connectionPhase: GameConnectionPhase.Detected,
                    canAttemptConnect: true,
                    eventMonitorSummary: GameConnectionEventMonitorSummary.ReadinessFailed("timeout")));

            Assert.Equal("CurrentGamePageStatusNotConnected", state.PageStatusText);
            AssertInactiveStats(state);
            AssertInactiveEvents(state);
        }

        [Fact]
        public void Project_WhenEventMonitorLoadingFailedBeforeConnect_ReturnsInactiveState()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);

            CurrentGamePageDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    null,
                    connectionPhase: GameConnectionPhase.Detected,
                    canAttemptConnect: true,
                    eventMonitorSummary: GameConnectionEventMonitorSummary.LoadingFailed("load failed")));

            Assert.Equal("CurrentGamePageStatusNotConnected", state.PageStatusText);
            AssertInactiveStats(state);
            AssertInactiveEvents(state);
        }

        [Fact]
        public void Project_WhenConnecting_ReturnsConnectingStateWithoutLiveStatsOrEvents()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);

            CurrentGamePageDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    CreateReadResult(detectedGame),
                    connectionPhase: GameConnectionPhase.Connecting,
                    eventMonitorSummary: GameConnectionEventMonitorSummary.Connecting));

            Assert.Equal("CurrentGamePageStatusConnecting", state.PageStatusText);
            AssertInactiveStats(state);
            AssertInactiveEvents(state);
        }

        [Fact]
        public void Project_WhenConnected_ReturnsStatsRoundAndRecentEvents()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);
            DateTimeOffset receivedAt = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
            GameEventMonitorStatus eventStatus = new(
                GameCompatibilityState.Compatible,
                DroppedEventCount: 2,
                DroppedNotifyCount: 3,
                PublishedNotifyCount: 4,
                RecentEvents:
                [
                    new GameEvent(GameEventType.StartOfRound, "start_of_round", 5, 0, 0, receivedAt),
                    new GameEvent(GameEventType.BoxEvent, "randomization_done", 5, 10, 20, receivedAt, "ray_gun_zm")
                ]);

            CurrentGamePageDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    CreateReadResult(detectedGame),
                    connectionPhase: GameConnectionPhase.Connected,
                    eventMonitorSummary: GameConnectionEventMonitorSummary.FromStatus(eventStatus)));

            Assert.Equal("CurrentGamePageStatusConnected", state.PageStatusText);
            Assert.Equal(1234.ToString("N0", CultureInfo.CurrentCulture), state.PointsText);
            Assert.Equal(5.ToString("N0", CultureInfo.CurrentCulture), state.KillsText);
            Assert.Equal(1.ToString("N0", CultureInfo.CurrentCulture), state.DownsText);
            Assert.Equal(2.ToString("N0", CultureInfo.CurrentCulture), state.RevivesText);
            Assert.Equal(3.ToString("N0", CultureInfo.CurrentCulture), state.HeadshotsText);
            Assert.Equal("CurrentRoundFormat(5, start_of_round)", state.CurrentRoundText);
            Assert.Contains("BoxEventWithWeaponFormat", state.BoxEventsText, StringComparison.Ordinal);
            Assert.Contains("randomization_done", state.BoxEventsText, StringComparison.Ordinal);
            Assert.Contains("Ray Gun (ray_gun_zm)", state.BoxEventsText, StringComparison.Ordinal);
            Assert.Contains("RecentEventFormat", state.RecentGameEventsText, StringComparison.Ordinal);
        }

        [Fact]
        public void Project_WhenConnectedWithPublishedEventsWithoutRoundOrBoxEvents_ReturnsEmptyEventSummaries()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);
            GameEventMonitorStatus eventStatus = new(
                GameCompatibilityState.Compatible,
                DroppedEventCount: 0,
                DroppedNotifyCount: 0,
                PublishedNotifyCount: 1,
                RecentEvents: []);

            CurrentGamePageDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    CreateReadResult(detectedGame),
                    connectionPhase: GameConnectionPhase.Connected,
                    eventMonitorSummary: GameConnectionEventMonitorSummary.FromStatus(eventStatus)));

            Assert.Equal("CurrentGamePageStatusConnected", state.PageStatusText);
            Assert.Equal(CurrentGamePageDisplayState.EmptyStatText, state.CurrentRoundText);
            Assert.Equal("RecentEventsEmpty", state.BoxEventsText);
            Assert.Equal("RecentEventsEmpty", state.RecentGameEventsText);
        }

        [Fact]
        public void Project_WhenDisconnecting_ReturnsDisconnectingCurrentGameState()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);

            CurrentGamePageDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    CreateReadResult(detectedGame),
                    connectionPhase: GameConnectionPhase.Disconnecting,
                    eventMonitorSummary: GameConnectionEventMonitorSummary.StopPending));

            Assert.Equal("CurrentGamePageStatusDisconnecting", state.PageStatusText);
            AssertInactiveStats(state);
            AssertInactiveEvents(state);
        }

        [Fact]
        public void DisplayStateContract_ExcludesCandidateAddressAndDebugFields()
        {
            PropertyInfo[] properties = typeof(CurrentGamePageDisplayState)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public);
            string[] excludedTerms =
            [
                "Candidate",
                "Address",
                "Button",
                "Command",
                "Connect",
                "Disconnect",
                "Debug",
                "Diagnostic",
                "Injection",
                "LowLevel",
                "Monitor",
                "Position"
            ];

            Assert.DoesNotContain(
                properties,
                property => excludedTerms.Any(term => property.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        private static CurrentGamePageDisplayProjector CreateProjector()
        {
            return new CurrentGamePageDisplayProjector();
        }

        private static GameConnectionSnapshot CreateSnapshot(
            DetectedGame? currentGame,
            PlayerStatsReadResult? readResult,
            bool canAttemptConnect = false,
            GameConnectionPhase? connectionPhase = null,
            GameConnectionEventMonitorSummary? eventMonitorSummary = null)
        {
            GameConnectionPhase phase = connectionPhase ?? DetermineConnectionPhase(currentGame);
            return new GameConnectionSnapshot(
                currentGame,
                phase,
                readResult,
                eventMonitorSummary ?? GameConnectionEventMonitorSummary.Waiting,
                CreateConnectCommandAvailability(phase, canAttemptConnect),
                CreateDisconnectCommandAvailability(phase));
        }

        private static GameConnectionCommandAvailability CreateConnectCommandAvailability(
            GameConnectionPhase connectionPhase,
            bool canAttemptConnect)
        {
            return connectionPhase switch
            {
                GameConnectionPhase.Connected or GameConnectionPhase.Disconnecting => GameConnectionCommandAvailability.Hidden,
                _ when canAttemptConnect => GameConnectionCommandAvailability.VisibleEnabled,
                _ => GameConnectionCommandAvailability.VisibleDisabled
            };
        }

        private static GameConnectionCommandAvailability CreateDisconnectCommandAvailability(
            GameConnectionPhase connectionPhase)
        {
            return connectionPhase == GameConnectionPhase.Connected
                ? GameConnectionCommandAvailability.VisibleEnabled
                : GameConnectionCommandAvailability.Hidden;
        }

        private static PlayerStatsReadResult CreateReadResult(DetectedGame detectedGame)
        {
            return new PlayerStatsReadResult(detectedGame, CreateStats());
        }

        private static GameConnectionPhase DetermineConnectionPhase(DetectedGame? currentGame)
        {
            if (currentGame is null)
            {
                return GameConnectionPhase.NoGame;
            }

            if (!currentGame.IsStatsSupported)
            {
                return GameConnectionPhase.UnsupportedGame;
            }

            return GameConnectionPhase.Detected;
        }

        private static PlayerStats CreateStats()
        {
            return new PlayerStats(
                1234,
                5,
                1,
                2,
                3,
                new PlayerCandidateStats(
                    PositionX: 12.345f,
                    PositionY: null,
                    PositionZ: -1.25f,
                    LegacyHealth: 100,
                    PlayerInfoHealth: null,
                    GEntityPlayerHealth: 90,
                    VelocityX: 1.5f,
                    VelocityY: null,
                    VelocityZ: null,
                    Gravity: 800,
                    Speed: null,
                    LastJumpHeight: null,
                    AdsAmount: null,
                    ViewAngleX: null,
                    ViewAngleY: null,
                    HeightInt: null,
                    HeightFloat: null,
                    AmmoSlot0: 30,
                    AmmoSlot1: null,
                    LethalAmmo: null,
                    AmmoSlot2: null,
                    TacticalAmmo: null,
                    AmmoSlot3: null,
                    AmmoSlot4: null,
                    AlternateKills: null,
                    AlternateHeadshots: null,
                    SecondaryKills: null,
                    SecondaryHeadshots: null,
                    Round: 7));
        }

        private static DetectedGame CreateSupportedGame(int processId)
        {
            return new DetectedGame(
                GameVariant.SteamZombies,
                "Steam Zombies",
                "t6zm",
                processId,
                PlayerStatAddressMap.SteamZombies,
                null);
        }

        private static DetectedGame CreateUnsupportedGame(int processId)
        {
            return new DetectedGame(
                GameVariant.RedactedZombies,
                "Redacted Zombies",
                "t6zm",
                processId,
                null,
                "Unsupported variant");
        }

        private static void AssertInactiveStats(CurrentGamePageDisplayState state)
        {
            Assert.Equal(CurrentGamePageDisplayState.EmptyStatText, state.PointsText);
            Assert.Equal(CurrentGamePageDisplayState.EmptyStatText, state.KillsText);
            Assert.Equal(CurrentGamePageDisplayState.EmptyStatText, state.DownsText);
            Assert.Equal(CurrentGamePageDisplayState.EmptyStatText, state.RevivesText);
            Assert.Equal(CurrentGamePageDisplayState.EmptyStatText, state.HeadshotsText);
        }

        private static void AssertInactiveEvents(CurrentGamePageDisplayState state)
        {
            Assert.Equal(CurrentGamePageDisplayState.EmptyStatText, state.CurrentRoundText);
            Assert.Equal("RecentEventsEmpty", state.BoxEventsText);
            Assert.Equal("RecentEventsEmpty", state.RecentGameEventsText);
        }
    }
}
