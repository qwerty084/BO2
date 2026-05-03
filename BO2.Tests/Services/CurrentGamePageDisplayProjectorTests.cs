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

            Assert.Equal("NoGameDetected", state.DetectedGameText);
            Assert.Equal("CurrentGamePageStatusNotConnected", state.PageStatusText);
            AssertInactiveStats(state);
            Assert.Equal("NoGameDetected", state.EventCompatibilityText);
            Assert.Equal("DllInjectionNotAttempted", state.InjectionStatusText);
            Assert.Equal("EventMonitorWaitingForMonitor", state.EventMonitorStatusText);
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

            Assert.Equal("Redacted Zombies", state.DetectedGameText);
            Assert.Equal("CurrentGamePageStatusNotConnected", state.PageStatusText);
            AssertInactiveStats(state);
            Assert.Equal("EventMonitorUnsupportedGameFormat(Redacted Zombies)", state.EventCompatibilityText);
            Assert.Equal("DllInjectionUnsupportedGameFormat(Redacted Zombies)", state.InjectionStatusText);
            Assert.Equal("EventMonitorCaptureDisabled", state.EventMonitorStatusText);
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

            Assert.Equal("Steam Zombies", state.DetectedGameText);
            Assert.Equal("CurrentGamePageStatusNotConnected", state.PageStatusText);
            AssertInactiveStats(state);
            Assert.Equal("GameProcessDetectorDisplayNameSteamZombies", state.EventCompatibilityText);
            Assert.Equal("DllInjectionWaitingForConnect", state.InjectionStatusText);
            Assert.Equal("EventMonitorWaitingForConnect", state.EventMonitorStatusText);
            AssertInactiveEvents(state);
        }

        [Fact]
        public void Project_WhenStatsOnlyDetectedBeforeConnect_ReturnsInactiveState()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);

            CurrentGamePageDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    CreateReadResult(detectedGame),
                    connectionPhase: GameConnectionPhase.StatsOnlyDetected,
                    canAttemptConnect: true));

            Assert.Equal("Steam Zombies", state.DetectedGameText);
            Assert.Equal("CurrentGamePageStatusNotConnected", state.PageStatusText);
            AssertInactiveStats(state);
            Assert.Equal("EventMonitorWaitingForConnect", state.EventMonitorStatusText);
            AssertInactiveEvents(state);
        }

        [Fact]
        public void Project_WhenStatsOnlyDetectedAfterDisconnect_ReturnsInactiveStateWithoutStaleEvents()
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
                    CreateReadResult(detectedGame),
                    connectionPhase: GameConnectionPhase.StatsOnlyDetected,
                    canAttemptConnect: true,
                    eventMonitorSummary: GameConnectionEventMonitorSummary.FromStatus(staleEventStatus)));

            Assert.Equal("Steam Zombies", state.DetectedGameText);
            Assert.Equal("CurrentGamePageStatusNotConnected", state.PageStatusText);
            AssertInactiveStats(state);
            Assert.Equal("EventMonitorWaitingForConnect", state.EventMonitorStatusText);
            AssertInactiveEvents(state);
        }

        [Fact]
        public void Project_WhenEventMonitorReadinessFailedBeforeConnect_ReturnsFailureStatus()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);

            CurrentGamePageDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    CreateReadResult(detectedGame),
                    connectionPhase: GameConnectionPhase.StatsOnlyDetected,
                    canAttemptConnect: true,
                    eventMonitorSummary: GameConnectionEventMonitorSummary.ReadinessFailed("timeout")));

            Assert.Equal("Steam Zombies", state.DetectedGameText);
            Assert.Equal("timeout", state.InjectionStatusText);
            Assert.Equal("EventMonitorWaitingForConnect", state.EventMonitorStatusText);
            AssertInactiveStats(state);
            AssertInactiveEvents(state);
        }

        [Fact]
        public void Project_WhenEventMonitorLoadingFailedBeforeConnect_ReturnsFailureStatus()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);

            CurrentGamePageDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    CreateReadResult(detectedGame),
                    connectionPhase: GameConnectionPhase.StatsOnlyDetected,
                    canAttemptConnect: true,
                    eventMonitorSummary: GameConnectionEventMonitorSummary.LoadingFailed("load failed")));

            Assert.Equal("Steam Zombies", state.DetectedGameText);
            Assert.Equal("load failed", state.InjectionStatusText);
            Assert.Equal("EventMonitorWaitingForConnect", state.EventMonitorStatusText);
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

            Assert.Equal("Steam Zombies", state.DetectedGameText);
            Assert.Equal("CurrentGamePageStatusConnecting", state.PageStatusText);
            AssertInactiveStats(state);
            Assert.Equal("DllInjectionConnecting", state.InjectionStatusText);
            Assert.Equal("EventMonitorWaitingForConnect", state.EventMonitorStatusText);
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

            Assert.Equal("Steam Zombies", state.DetectedGameText);
            Assert.Equal("CurrentGamePageStatusConnected", state.PageStatusText);
            Assert.Equal(1234.ToString("N0", CultureInfo.CurrentCulture), state.PointsText);
            Assert.Equal("DllInjectionMonitorReady", state.InjectionStatusText);
            Assert.Equal("EventMonitorCaptureDropsFormat(EventMonitorCompatible, 2, 3, 4)", state.EventMonitorStatusText);
            Assert.Equal("CurrentRoundFormat(5, start_of_round)", state.CurrentRoundText);
            Assert.Contains("BoxEventWithWeaponFormat", state.BoxEventsText, StringComparison.Ordinal);
            Assert.Contains("randomization_done", state.BoxEventsText, StringComparison.Ordinal);
            Assert.Contains("Ray Gun (ray_gun_zm)", state.BoxEventsText, StringComparison.Ordinal);
            Assert.Contains("RecentEventFormat", state.RecentGameEventsText, StringComparison.Ordinal);
        }

        [Fact]
        public void Project_WhenConnectedWithPublishedEvents_ReturnsPublishedMonitorStatus()
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

            Assert.Equal("DllInjectionMonitorReady", state.InjectionStatusText);
            Assert.Equal("EventMonitorPublishedEventsFormat(EventMonitorCompatible, 1)", state.EventMonitorStatusText);
            Assert.Equal(CurrentGamePageDisplayState.EmptyStatText, state.CurrentRoundText);
            Assert.Equal("RecentEventsEmpty", state.BoxEventsText);
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

            Assert.Equal("Steam Zombies", state.DetectedGameText);
            Assert.Equal("CurrentGamePageStatusDisconnecting", state.PageStatusText);
            AssertInactiveStats(state);
            Assert.Equal("DllInjectionDisconnecting", state.InjectionStatusText);
            Assert.Equal("EventMonitorDisconnecting", state.EventMonitorStatusText);
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
            GameConnectionPhase phase = connectionPhase ?? DetermineConnectionPhase(
                currentGame,
                readResult);
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

        private static GameConnectionPhase DetermineConnectionPhase(
            DetectedGame? currentGame,
            PlayerStatsReadResult? readResult)
        {
            if (currentGame is null)
            {
                return GameConnectionPhase.NoGame;
            }

            if (!currentGame.IsStatsSupported)
            {
                return GameConnectionPhase.UnsupportedGame;
            }

            return readResult is not null
                && readResult.DetectedGame.ProcessId == currentGame.ProcessId
                && readResult.Stats is not null
                    ? GameConnectionPhase.StatsOnlyDetected
                    : GameConnectionPhase.Detected;
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
