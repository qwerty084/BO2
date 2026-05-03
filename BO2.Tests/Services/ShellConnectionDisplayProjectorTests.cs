using System;
using System.Linq;
using System.Reflection;
using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class ShellConnectionDisplayProjectorTests
    {
        [Fact]
        public void Project_WhenNoGame_ReturnsDisconnectedShellState()
        {
            ShellConnectionDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    null,
                    connectionPhase: GameConnectionPhase.NoGame));

            Assert.Equal("NoGameDetected", state.DetectedGameText);
            Assert.Equal("EventMonitorWaitingForMonitor", state.EventMonitorStatusText);
            Assert.Equal("GameNotRunning", state.MainStatusText);
            Assert.Equal("FooterGameNotRunning", state.FooterGameStatusText);
            Assert.Equal("FooterEventsNotConnected", state.FooterEventStatusText);
            Assert.Equal("ConnectionCardStatusDisconnected", state.ConnectionCardStatusText);
            Assert.Equal(ShellConnectionDisplayState.EmptyText, state.ConnectionLastUpdateText);
            Assert.Equal("ConnectButtonWaitingForGameText", state.ConnectButtonText);
            Assert.Equal("DisconnectButtonText", state.DisconnectButtonText);
            Assert.False(state.IsConnectCommandEnabled);
            Assert.True(state.IsConnectCommandVisible);
            Assert.False(state.IsDisconnectCommandEnabled);
            Assert.False(state.IsDisconnectCommandVisible);
            Assert.False(state.IsFooterSuccessIndicatorVisible);
            Assert.False(state.IsFooterPendingIndicatorVisible);
            Assert.True(state.IsFooterDisconnectedIndicatorVisible);
            Assert.False(state.IsFooterErrorIndicatorVisible);
            Assert.Same(GameEventMonitorStatus.WaitingForMonitor, state.LatestEventStatus);
        }

        [Fact]
        public void Project_WhenUnsupportedGame_ReturnsUnsupportedShellState()
        {
            DetectedGame detectedGame = CreateUnsupportedGame(1001);

            ShellConnectionDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    connectionPhase: GameConnectionPhase.UnsupportedGame));

            Assert.Equal("Redacted Zombies", state.DetectedGameText);
            Assert.Equal("EventMonitorCaptureDisabled", state.EventMonitorStatusText);
            Assert.Equal("UnsupportedStatusWithReasonFormat(Redacted Zombies, Unsupported variant)", state.MainStatusText);
            Assert.Equal("FooterGameDetectedFormat(Redacted Zombies)", state.FooterGameStatusText);
            Assert.Equal("FooterEventsUnsupported", state.FooterEventStatusText);
            Assert.Equal("ConnectionCardStatusUnsupported", state.ConnectionCardStatusText);
            Assert.Equal(ShellConnectionDisplayState.EmptyText, state.ConnectionLastUpdateText);
            Assert.Equal("ConnectButtonUnsupportedText", state.ConnectButtonText);
            Assert.Equal("DisconnectButtonText", state.DisconnectButtonText);
            Assert.False(state.IsConnectCommandEnabled);
            Assert.True(state.IsConnectCommandVisible);
            Assert.False(state.IsDisconnectCommandEnabled);
            Assert.False(state.IsDisconnectCommandVisible);
            Assert.False(state.IsFooterSuccessIndicatorVisible);
            Assert.True(state.IsFooterPendingIndicatorVisible);
            Assert.False(state.IsFooterDisconnectedIndicatorVisible);
            Assert.False(state.IsFooterErrorIndicatorVisible);
            Assert.Same(GameEventMonitorStatus.WaitingForMonitor, state.LatestEventStatus);
        }

        [Fact]
        public void Project_WhenDetectedGameCanConnect_ReturnsDetectedShellState()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);

            ShellConnectionDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    canAttemptConnect: true,
                    connectionPhase: GameConnectionPhase.Detected));

            Assert.Equal("Steam Zombies", state.DetectedGameText);
            Assert.Equal("EventMonitorWaitingForConnect", state.EventMonitorStatusText);
            Assert.Equal("GameDetectedConnectPromptFormat(Steam Zombies)", state.MainStatusText);
            Assert.Equal("FooterGameDetectedFormat(Steam Zombies)", state.FooterGameStatusText);
            Assert.Equal("FooterEventsNotConnected", state.FooterEventStatusText);
            Assert.Equal("ConnectionCardStatusMonitoring", state.ConnectionCardStatusText);
            Assert.Equal(ShellConnectionDisplayState.EmptyText, state.ConnectionLastUpdateText);
            Assert.Equal("ConnectButtonText", state.ConnectButtonText);
            Assert.Equal("DisconnectButtonText", state.DisconnectButtonText);
            Assert.True(state.IsConnectCommandEnabled);
            Assert.True(state.IsConnectCommandVisible);
            Assert.False(state.IsDisconnectCommandEnabled);
            Assert.False(state.IsDisconnectCommandVisible);
            Assert.False(state.IsFooterSuccessIndicatorVisible);
            Assert.True(state.IsFooterPendingIndicatorVisible);
            Assert.False(state.IsFooterDisconnectedIndicatorVisible);
            Assert.False(state.IsFooterErrorIndicatorVisible);
        }

        [Fact]
        public void Project_WhenConnecting_ReturnsConnectingShellState()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);

            ShellConnectionDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    connectionPhase: GameConnectionPhase.Connecting,
                    eventMonitorSummary: GameConnectionEventMonitorSummary.Connecting));

            Assert.Equal("Steam Zombies", state.DetectedGameText);
            Assert.Equal("EventMonitorWaitingForConnect", state.EventMonitorStatusText);
            Assert.Equal("ConnectionStatusConnecting", state.MainStatusText);
            Assert.Equal("FooterGameDetectedFormat(Steam Zombies)", state.FooterGameStatusText);
            Assert.Equal("FooterEventsConnecting", state.FooterEventStatusText);
            Assert.Equal("ConnectionCardStatusConnecting", state.ConnectionCardStatusText);
            Assert.Equal(ShellConnectionDisplayState.EmptyText, state.ConnectionLastUpdateText);
            Assert.Equal("ConnectButtonConnectingText", state.ConnectButtonText);
            Assert.Equal("DisconnectButtonText", state.DisconnectButtonText);
            Assert.False(state.IsConnectCommandEnabled);
            Assert.True(state.IsConnectCommandVisible);
            Assert.False(state.IsDisconnectCommandEnabled);
            Assert.False(state.IsDisconnectCommandVisible);
            Assert.False(state.IsFooterSuccessIndicatorVisible);
            Assert.True(state.IsFooterPendingIndicatorVisible);
            Assert.False(state.IsFooterDisconnectedIndicatorVisible);
            Assert.False(state.IsFooterErrorIndicatorVisible);
            Assert.Same(GameEventMonitorStatus.WaitingForMonitor, state.LatestEventStatus);
        }

        [Fact]
        public void Project_WhenConnected_ReturnsConnectedShellState()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);
            GameEventMonitorStatus eventStatus = new(
                GameCompatibilityState.Compatible,
                DroppedEventCount: 2,
                DroppedNotifyCount: 3,
                PublishedNotifyCount: 4,
                RecentEvents: []);

            ShellConnectionDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    connectionPhase: GameConnectionPhase.Connected,
                    eventMonitorSummary: GameConnectionEventMonitorSummary.FromStatus(eventStatus)));

            Assert.Equal("Steam Zombies", state.DetectedGameText);
            Assert.Equal("EventMonitorCaptureDropsFormat(EventMonitorCompatible, 2, 3, 4)", state.EventMonitorStatusText);
            Assert.Equal("ConnectedStatusFormat(Steam Zombies)", state.MainStatusText);
            Assert.Equal("FooterGameDetectedFormat(Steam Zombies)", state.FooterGameStatusText);
            Assert.Equal("FooterEventsConnected", state.FooterEventStatusText);
            Assert.Equal("ConnectionCardStatusConnected", state.ConnectionCardStatusText);
            Assert.Equal("ConnectionLastUpdateJustNow", state.ConnectionLastUpdateText);
            Assert.Equal("ConnectButtonConnectedText", state.ConnectButtonText);
            Assert.Equal("DisconnectButtonText", state.DisconnectButtonText);
            Assert.False(state.IsConnectCommandEnabled);
            Assert.False(state.IsConnectCommandVisible);
            Assert.True(state.IsDisconnectCommandEnabled);
            Assert.True(state.IsDisconnectCommandVisible);
            Assert.True(state.IsFooterSuccessIndicatorVisible);
            Assert.False(state.IsFooterPendingIndicatorVisible);
            Assert.False(state.IsFooterDisconnectedIndicatorVisible);
            Assert.False(state.IsFooterErrorIndicatorVisible);
            Assert.Same(eventStatus, state.LatestEventStatus);
        }

        [Fact]
        public void Project_WhenDisconnecting_ReturnsDisconnectingShellState()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);

            ShellConnectionDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    connectionPhase: GameConnectionPhase.Disconnecting,
                    eventMonitorSummary: GameConnectionEventMonitorSummary.StopPending));

            Assert.Equal("Steam Zombies", state.DetectedGameText);
            Assert.Equal("EventMonitorDisconnecting", state.EventMonitorStatusText);
            Assert.Equal("ConnectionStatusDisconnecting", state.MainStatusText);
            Assert.Equal("FooterGameDetectedFormat(Steam Zombies)", state.FooterGameStatusText);
            Assert.Equal("FooterEventsDisconnecting", state.FooterEventStatusText);
            Assert.Equal("ConnectionCardStatusDisconnecting", state.ConnectionCardStatusText);
            Assert.Equal(ShellConnectionDisplayState.EmptyText, state.ConnectionLastUpdateText);
            Assert.Equal("ConnectionCardStatusDisconnecting", state.ConnectButtonText);
            Assert.Equal("DisconnectButtonText", state.DisconnectButtonText);
            Assert.False(state.IsConnectCommandEnabled);
            Assert.False(state.IsConnectCommandVisible);
            Assert.False(state.IsDisconnectCommandEnabled);
            Assert.False(state.IsDisconnectCommandVisible);
            Assert.False(state.IsFooterSuccessIndicatorVisible);
            Assert.True(state.IsFooterPendingIndicatorVisible);
            Assert.False(state.IsFooterDisconnectedIndicatorVisible);
            Assert.False(state.IsFooterErrorIndicatorVisible);
            Assert.Same(GameEventMonitorStatus.WaitingForMonitor, state.LatestEventStatus);
        }

        [Fact]
        public void Project_UsesSnapshotCommandAvailabilityForCommandState()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);

            ShellConnectionDisplayState state = CreateProjector().Project(
                CreateSnapshot(
                    detectedGame,
                    connectionPhase: GameConnectionPhase.Connected,
                    connectCommandAvailability: GameConnectionCommandAvailability.VisibleEnabled,
                    disconnectCommandAvailability: GameConnectionCommandAvailability.Hidden));

            Assert.True(state.IsConnectCommandEnabled);
            Assert.True(state.IsConnectCommandVisible);
            Assert.False(state.IsDisconnectCommandEnabled);
            Assert.False(state.IsDisconnectCommandVisible);
        }

        [Fact]
        public void DisplayStateContract_IncludesShellFieldsOnly()
        {
            string[] propertyNames = typeof(ShellConnectionDisplayState)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(property => property.Name)
                .ToArray();
            string[] requiredProperties =
            [
                nameof(ShellConnectionDisplayState.DetectedGameText),
                nameof(ShellConnectionDisplayState.EventMonitorStatusText),
                nameof(ShellConnectionDisplayState.MainStatusText),
                nameof(ShellConnectionDisplayState.FooterGameStatusText),
                nameof(ShellConnectionDisplayState.FooterEventStatusText),
                nameof(ShellConnectionDisplayState.ConnectionCardStatusText),
                nameof(ShellConnectionDisplayState.ConnectionLastUpdateText),
                nameof(ShellConnectionDisplayState.ConnectButtonText),
                nameof(ShellConnectionDisplayState.DisconnectButtonText),
                nameof(ShellConnectionDisplayState.IsConnectCommandEnabled),
                nameof(ShellConnectionDisplayState.IsConnectCommandVisible),
                nameof(ShellConnectionDisplayState.IsDisconnectCommandEnabled),
                nameof(ShellConnectionDisplayState.IsDisconnectCommandVisible),
                nameof(ShellConnectionDisplayState.IsFooterSuccessIndicatorVisible),
                nameof(ShellConnectionDisplayState.IsFooterPendingIndicatorVisible),
                nameof(ShellConnectionDisplayState.IsFooterDisconnectedIndicatorVisible),
                nameof(ShellConnectionDisplayState.IsFooterErrorIndicatorVisible),
                nameof(ShellConnectionDisplayState.LatestEventStatus)
            ];

            Assert.Subset(propertyNames.ToHashSet(StringComparer.Ordinal), requiredProperties.ToHashSet(StringComparer.Ordinal));
            Assert.DoesNotContain(propertyNames, property => property.Contains("Point", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(propertyNames, property => property.Contains("Candidate", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(propertyNames, property => property.Contains("Round", StringComparison.OrdinalIgnoreCase));
        }

        private static ShellConnectionDisplayProjector CreateProjector()
        {
            return new ShellConnectionDisplayProjector();
        }

        private static GameConnectionSnapshot CreateSnapshot(
            DetectedGame? currentGame,
            bool canAttemptConnect = false,
            GameConnectionPhase? connectionPhase = null,
            GameConnectionCommandAvailability? connectCommandAvailability = null,
            GameConnectionCommandAvailability? disconnectCommandAvailability = null,
            GameConnectionEventMonitorSummary? eventMonitorSummary = null)
        {
            GameConnectionPhase phase = connectionPhase ?? DetermineConnectionPhase(currentGame);
            return new GameConnectionSnapshot(
                currentGame,
                phase,
                null,
                eventMonitorSummary ?? GameConnectionEventMonitorSummary.Waiting,
                connectCommandAvailability ?? CreateConnectCommandAvailability(phase, canAttemptConnect),
                disconnectCommandAvailability ?? CreateDisconnectCommandAvailability(phase));
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

        private static GameConnectionPhase DetermineConnectionPhase(DetectedGame? currentGame)
        {
            if (currentGame is null)
            {
                return GameConnectionPhase.NoGame;
            }

            return currentGame.IsStatsSupported
                ? GameConnectionPhase.Detected
                : GameConnectionPhase.UnsupportedGame;
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
    }
}
