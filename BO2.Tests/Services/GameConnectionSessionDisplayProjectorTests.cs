using System;
using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameConnectionSessionDisplayProjectorTests
    {
        [Fact]
        public void Project_WhenNoGame_ReturnsDisconnectedDisplayProjection()
        {
            GameConnectionSessionDisplayProjection projection = CreateProjector().Project(
                CreateSnapshot(
                    null,
                    null,
                    connectionPhase: GameConnectionPhase.NoGame));

            AssertResource("NoGameDetected", projection.DetectedGameText);
            AssertResource("GameNotRunning", projection.StatusText);
            AssertResource("FooterGameNotRunning", projection.GameStatusText);
            AssertResource("FooterEventsNotConnected", projection.EventConnectionStatusText);
            AssertResource("ConnectButtonWaitingForGameText", projection.ConnectButtonText);
            Assert.False(projection.IsConnectButtonEnabled);
            Assert.True(projection.IsConnectButtonVisible);
            Assert.False(projection.IsDisconnectButtonVisible);
            Assert.True(projection.IsFooterDisconnectedStatusVisible);
            AssertPlain("--", projection.PointsText);
        }

        [Fact]
        public void Project_WhenUnsupportedGame_ReturnsUnsupportedDisplayProjection()
        {
            DetectedGame detectedGame = CreateUnsupportedGame(1001);

            GameConnectionSessionDisplayProjection projection = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    null,
                    connectionPhase: GameConnectionPhase.UnsupportedGame));

            AssertPlain("Redacted Zombies", projection.DetectedGameText);
            DisplayText.FormatText status = AssertFormat("UnsupportedStatusWithReasonFormat", projection.StatusText);
            AssertPlain("Redacted Zombies", status.Arguments[0]);
            AssertPlain("Unsupported variant", status.Arguments[1]);
            DisplayText.FormatText injectionStatus = AssertFormat("DllInjectionUnsupportedGameFormat", projection.InjectionStatusText);
            AssertPlain("Redacted Zombies", injectionStatus.Arguments[0]);
            AssertResource("FooterEventsUnsupported", projection.EventConnectionStatusText);
            AssertResource("ConnectButtonUnsupportedText", projection.ConnectButtonText);
            Assert.False(projection.IsConnectButtonEnabled);
            Assert.True(projection.IsFooterPendingStatusVisible);
            AssertPlain("--", projection.PointsText);
        }

        [Fact]
        public void Project_WhenDetectedWithoutStats_ReturnsDetectedDisplayProjection()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);

            GameConnectionSessionDisplayProjection projection = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    null,
                    canAttemptConnect: true,
                    connectionPhase: GameConnectionPhase.Detected));

            AssertPlain("Steam Zombies", projection.DetectedGameText);
            DisplayText.FormatText status = AssertFormat("GameDetectedConnectPromptFormat", projection.StatusText);
            AssertPlain("Steam Zombies", status.Arguments[0]);
            AssertResource("DllInjectionWaitingForConnect", projection.InjectionStatusText);
            AssertResource("EventMonitorWaitingForConnect", projection.EventMonitorStatusText);
            AssertResource("ConnectButtonText", projection.ConnectButtonText);
            Assert.True(projection.IsConnectButtonEnabled);
            Assert.True(projection.IsFooterPendingStatusVisible);
            AssertPlain("--", projection.PointsText);
        }

        [Fact]
        public void Project_WhenSupportedStatsWithoutMonitor_ReturnsStatsAndConnectPrompt()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);
            PlayerStats stats = CreateStats();
            var readResult = new PlayerStatsReadResult(
                detectedGame,
                stats);

            GameConnectionSessionDisplayProjection projection = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    readResult,
                    connectionPhase: GameConnectionPhase.StatsOnlyDetected,
                    canAttemptConnect: true));

            AssertPlain("Steam Zombies", projection.DetectedGameText);
            DisplayText.FormatText status = AssertFormat("GameDetectedConnectPromptFormat", projection.StatusText);
            AssertPlain("Steam Zombies", status.Arguments[0]);
            AssertInteger(1234, projection.PointsText);
            AssertFloat2(12.345f, projection.PositionXText);

            DisplayText.LinesText playerLines = AssertLines(projection.PlayerCandidateDetailsText);
            DisplayText.FormatText velocityLine = AssertFormat("LabeledValueFormat", playerLines.Items[0]);
            AssertResource("VelocityXLabel", velocityLine.Arguments[0]);
            AssertFloat2(1.5f, velocityLine.Arguments[1]);

            DisplayText.LinesText addressLines = AssertLines(projection.AddressCandidateDetailsText);
            DisplayText.FormatText addressLine = AssertFormat("LabeledValueFormat", addressLines.Items[0]);
            AssertResource("LocalPlayerBaseLabel", addressLine.Arguments[0]);
            Assert.IsType<DisplayText.AddressText>(addressLine.Arguments[1]);

            DisplayText.FormatText footerGame = AssertFormat("FooterGameDetectedFormat", projection.GameStatusText);
            AssertPlain("Steam Zombies", footerGame.Arguments[0]);
            AssertResource("FooterEventsNotConnected", projection.EventConnectionStatusText);
            AssertResource("ConnectButtonText", projection.ConnectButtonText);
            Assert.True(projection.IsConnectButtonEnabled);
            Assert.True(projection.IsConnectButtonVisible);
            Assert.False(projection.IsDisconnectButtonVisible);
            Assert.True(projection.IsFooterPendingStatusVisible);
        }

        [Fact]
        public void Project_WhenMonitorConnectedWithEvents_ReturnsMonitorDisplayProjection()
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
            var readResult = new PlayerStatsReadResult(
                detectedGame,
                CreateStats());

            GameConnectionSessionDisplayProjection projection = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    readResult,
                    eventStatus,
                    new DllInjectionResult(DllInjectionState.Loaded, "Loaded"),
                    hasInjectionAttemptForCurrentGame: true,
                    isMonitorConnectedForCurrentGame: true));

            DisplayText.FormatText status = AssertFormat("ConnectedStatusFormat", projection.StatusText);
            AssertPlain("Steam Zombies", status.Arguments[0]);
            AssertResource("DllInjectionMonitorReady", projection.InjectionStatusText);
            DisplayText.FormatText monitorStatus = AssertFormat("EventMonitorCaptureDropsFormat", projection.EventMonitorStatusText);
            AssertResource("EventMonitorCompatible", monitorStatus.Arguments[0]);
            Assert.Equal(2u, monitorStatus.Arguments[1]);
            Assert.Equal(3u, monitorStatus.Arguments[2]);
            Assert.Equal(4u, monitorStatus.Arguments[3]);

            DisplayText.FormatText currentRound = AssertFormat("CurrentRoundFormat", projection.CurrentRoundText);
            Assert.Equal(5, currentRound.Arguments[0]);
            AssertPlain("start_of_round", currentRound.Arguments[1]);

            DisplayText.LinesText boxEvents = AssertLines(projection.BoxEventsText);
            DisplayText.FormatText boxEvent = AssertFormat("BoxEventWithWeaponFormat", boxEvents.Items[0]);
            Assert.IsType<DisplayText.LocalTimeText>(boxEvent.Arguments[0]);
            Assert.Equal("randomization_done", boxEvent.Arguments[1]);
            Assert.Equal("Ray Gun (ray_gun_zm)", boxEvent.Arguments[2]);

            DisplayText.LinesText recentEvents = AssertLines(projection.RecentGameEventsText);
            AssertFormat("RecentEventFormat", recentEvents.Items[0]);
            AssertResource("ConnectButtonConnectedText", projection.ConnectButtonText);
            Assert.False(projection.IsConnectButtonVisible);
            Assert.True(projection.IsDisconnectButtonVisible);
            Assert.True(projection.IsFooterSuccessStatusVisible);
            Assert.Same(eventStatus, projection.LatestEventStatus);
        }

        [Fact]
        public void Project_WhenDisconnecting_ReturnsDisconnectingDisplayProjection()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);
            var readResult = new PlayerStatsReadResult(
                detectedGame,
                CreateStats());

            GameConnectionSessionDisplayProjection projection = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    readResult,
                    new GameEventMonitorStatus(
                        GameCompatibilityState.Compatible,
                        DroppedEventCount: 0,
                        DroppedNotifyCount: 0,
                        PublishedNotifyCount: 0,
                        RecentEvents: []),
                    new DllInjectionResult(DllInjectionState.Loaded, "Loaded"),
                    isDisconnecting: true,
                    hasInjectionAttemptForCurrentGame: true,
                    isMonitorConnectedForCurrentGame: true));

            AssertResource("ConnectionStatusDisconnecting", projection.StatusText);
            AssertResource("DllInjectionDisconnecting", projection.InjectionStatusText);
            AssertResource("EventMonitorDisconnecting", projection.EventMonitorStatusText);
            AssertPlain("--", projection.CurrentRoundText);
            AssertResource("ConnectionCardStatusDisconnecting", projection.ConnectButtonText);
            Assert.False(projection.IsConnectButtonEnabled);
            Assert.False(projection.IsConnectButtonVisible);
            Assert.False(projection.IsDisconnectButtonVisible);
            Assert.True(projection.IsFooterPendingStatusVisible);
            Assert.Same(GameEventMonitorStatus.WaitingForMonitor, projection.LatestEventStatus);
        }

        private static GameConnectionSessionDisplayProjector CreateProjector()
        {
            return new GameConnectionSessionDisplayProjector();
        }

        private static GameConnectionSnapshot CreateSnapshot(
            DetectedGame? currentGame,
            PlayerStatsReadResult? readResult,
            GameEventMonitorStatus? eventStatus = null,
            DllInjectionResult? injectionResult = null,
            bool isConnecting = false,
            bool isDisconnecting = false,
            bool canAttemptConnect = false,
            bool hasInjectionAttemptForCurrentGame = false,
            bool isMonitorConnectedForCurrentGame = false,
            GameConnectionPhase? connectionPhase = null)
        {
            return new GameConnectionSnapshot(
                currentGame,
                connectionPhase ?? DetermineConnectionPhase(
                    currentGame,
                    readResult,
                    isConnecting,
                    isDisconnecting,
                    isMonitorConnectedForCurrentGame),
                readResult,
                eventStatus ?? GameEventMonitorStatus.WaitingForMonitor,
                injectionResult ?? DllInjectionResult.NotAttempted,
                isConnecting,
                isDisconnecting,
                canAttemptConnect,
                hasInjectionAttemptForCurrentGame,
                isMonitorConnectedForCurrentGame);
        }

        private static GameConnectionPhase DetermineConnectionPhase(
            DetectedGame? currentGame,
            PlayerStatsReadResult? readResult,
            bool isConnecting,
            bool isDisconnecting,
            bool isMonitorConnectedForCurrentGame)
        {
            if (currentGame is null)
            {
                return GameConnectionPhase.NoGame;
            }

            if (!currentGame.IsStatsSupported)
            {
                return GameConnectionPhase.UnsupportedGame;
            }

            if (isDisconnecting)
            {
                return GameConnectionPhase.Disconnecting;
            }

            if (isConnecting)
            {
                return GameConnectionPhase.Connecting;
            }

            if (isMonitorConnectedForCurrentGame)
            {
                return GameConnectionPhase.Connected;
            }

            return readResult?.DetectedGame.ProcessId == currentGame.ProcessId
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

        private static DisplayText.FormatText AssertFormat(string resourceId, DisplayText text)
        {
            DisplayText.FormatText format = Assert.IsType<DisplayText.FormatText>(text);
            Assert.Equal(resourceId, format.ResourceId);
            return format;
        }

        private static DisplayText.LinesText AssertLines(DisplayText text)
        {
            return Assert.IsType<DisplayText.LinesText>(text);
        }

        private static void AssertResource(string resourceId, DisplayText text)
        {
            DisplayText.ResourceText resource = Assert.IsType<DisplayText.ResourceText>(text);
            Assert.Equal(resourceId, resource.ResourceId);
        }

        private static void AssertResource(string resourceId, object text)
        {
            AssertResource(resourceId, Assert.IsType<DisplayText>(text, exactMatch: false));
        }

        private static void AssertPlain(string value, DisplayText text)
        {
            DisplayText.PlainText plain = Assert.IsType<DisplayText.PlainText>(text);
            Assert.Equal(value, plain.Text);
        }

        private static void AssertPlain(string value, object text)
        {
            AssertPlain(value, Assert.IsType<DisplayText>(text, exactMatch: false));
        }

        private static void AssertInteger(int value, DisplayText text)
        {
            DisplayText.IntegerText integer = Assert.IsType<DisplayText.IntegerText>(text);
            Assert.Equal(value, integer.Value);
        }

        private static void AssertFloat2(float value, DisplayText text)
        {
            DisplayText.Float2Text float2 = Assert.IsType<DisplayText.Float2Text>(text);
            Assert.Equal(value, float2.Value);
        }

        private static void AssertFloat2(float value, object text)
        {
            AssertFloat2(value, Assert.IsType<DisplayText>(text, exactMatch: false));
        }
    }
}
