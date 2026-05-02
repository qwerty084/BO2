using System;
using System.Globalization;
using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameConnectionSessionDisplayProjectorTests
    {
        [Fact]
        public void Project_WhenNoGame_ReturnsDisconnectedDisplayState()
        {
            GameConnectionSessionDisplayState state = CreateProjector().Project(
                CreateSnapshot(null, PlayerStatsReadResult.GameNotRunning));

            Assert.Equal("NoGameDetected", state.DetectedGameText);
            Assert.Equal("GameNotRunning", state.StatusText);
            Assert.Equal("FooterGameNotRunning", state.GameStatusText);
            Assert.Equal("FooterEventsNotConnected", state.EventConnectionStatusText);
            Assert.Equal("ConnectButtonWaitingForGameText", state.ConnectButtonText);
            Assert.False(state.IsConnectButtonEnabled);
            Assert.True(state.IsConnectButtonVisible);
            Assert.False(state.IsDisconnectButtonVisible);
            Assert.True(state.IsFooterDisconnectedStatusVisible);
            Assert.Equal(GameConnectionSessionDisplayState.EmptyStatText, state.PointsText);
        }

        [Fact]
        public void Project_WhenSupportedStatsWithoutMonitor_ReturnsStatsAndConnectPrompt()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);
            PlayerStats stats = CreateStats();
            var readResult = new PlayerStatsReadResult(
                detectedGame,
                stats,
                "ConnectedStatus",
                ConnectionState.Connected);

            GameConnectionSessionDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    readResult,
                    canAttemptConnect: true));

            Assert.Equal("Steam Zombies", state.DetectedGameText);
            Assert.Equal("GameDetectedConnectPromptFormat(Steam Zombies)", state.StatusText);
            Assert.Equal(1234.ToString("N0", CultureInfo.CurrentCulture), state.PointsText);
            Assert.Equal(12.345f.ToString("N2", CultureInfo.CurrentCulture), state.PositionXText);
            Assert.Contains("VelocityXLabel", state.PlayerCandidateDetailsText, StringComparison.Ordinal);
            Assert.Contains("LocalPlayerBaseLabel", state.AddressCandidateDetailsText, StringComparison.Ordinal);
            Assert.Equal("FooterGameDetectedFormat(Steam Zombies)", state.GameStatusText);
            Assert.Equal("FooterEventsNotConnected", state.EventConnectionStatusText);
            Assert.Equal("ConnectButtonText", state.ConnectButtonText);
            Assert.True(state.IsConnectButtonEnabled);
            Assert.True(state.IsConnectButtonVisible);
            Assert.False(state.IsDisconnectButtonVisible);
            Assert.True(state.IsFooterPendingStatusVisible);
        }

        [Fact]
        public void Project_WhenMonitorConnectedWithEvents_ReturnsMonitorDisplayState()
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
                CreateStats(),
                "ConnectedStatus",
                ConnectionState.Connected);

            GameConnectionSessionDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    readResult,
                    eventStatus,
                    new DllInjectionResult(DllInjectionState.Loaded, "Loaded"),
                    hasInjectionAttemptForCurrentGame: true,
                    isMonitorConnectedForCurrentGame: true));

            Assert.Equal("ConnectedStatus", state.StatusText);
            Assert.Equal("DllInjectionMonitorReady", state.InjectionStatusText);
            Assert.Equal("EventMonitorCaptureDropsFormat(EventMonitorCompatible, 2, 3, 4)", state.EventMonitorStatusText);
            Assert.Equal("CurrentRoundFormat(5, start_of_round)", state.CurrentRoundText);
            Assert.Contains("BoxEventWithWeaponFormat", state.BoxEventsText, StringComparison.Ordinal);
            Assert.Contains("Ray Gun (ray_gun_zm)", state.BoxEventsText, StringComparison.Ordinal);
            Assert.Contains("RecentEventFormat", state.RecentGameEventsText, StringComparison.Ordinal);
            Assert.Equal("ConnectButtonConnectedText", state.ConnectButtonText);
            Assert.False(state.IsConnectButtonVisible);
            Assert.True(state.IsDisconnectButtonVisible);
            Assert.True(state.IsFooterSuccessStatusVisible);
            Assert.Same(eventStatus, state.LatestEventStatus);
        }

        [Fact]
        public void Project_WhenDisconnecting_ReturnsDisconnectingDisplayState()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);
            var readResult = new PlayerStatsReadResult(
                detectedGame,
                null,
                "ConnectedStatus",
                ConnectionState.Connected);

            GameConnectionSessionDisplayState state = CreateProjector().Project(
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

            Assert.Equal("ConnectionStatusDisconnecting", state.StatusText);
            Assert.Equal("DllInjectionDisconnecting", state.InjectionStatusText);
            Assert.Equal("EventMonitorDisconnecting", state.EventMonitorStatusText);
            Assert.Equal(GameConnectionSessionDisplayState.EmptyStatText, state.CurrentRoundText);
            Assert.Equal("ConnectionCardStatusDisconnecting", state.ConnectButtonText);
            Assert.False(state.IsConnectButtonEnabled);
            Assert.False(state.IsConnectButtonVisible);
            Assert.False(state.IsDisconnectButtonVisible);
            Assert.True(state.IsFooterPendingStatusVisible);
            Assert.Same(GameEventMonitorStatus.WaitingForMonitor, state.LatestEventStatus);
        }

        private static GameConnectionSessionDisplayProjector CreateProjector()
        {
            return new GameConnectionSessionDisplayProjector(AppStrings.Get("UnavailableValue"));
        }

        private static GameConnectionRefreshResult CreateSnapshot(
            DetectedGame? currentGame,
            PlayerStatsReadResult readResult,
            GameEventMonitorStatus? eventStatus = null,
            DllInjectionResult? injectionResult = null,
            bool isConnecting = false,
            bool isDisconnecting = false,
            bool canAttemptConnect = false,
            bool hasInjectionAttemptForCurrentGame = false,
            bool isMonitorConnectedForCurrentGame = false)
        {
            return new GameConnectionRefreshResult(
                currentGame,
                readResult,
                eventStatus ?? GameEventMonitorStatus.WaitingForMonitor,
                injectionResult ?? DllInjectionResult.NotAttempted,
                isConnecting,
                isDisconnecting,
                canAttemptConnect,
                hasInjectionAttemptForCurrentGame,
                isMonitorConnectedForCurrentGame);
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
    }
}
