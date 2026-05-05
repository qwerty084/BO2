using System;

namespace BO2.Services
{
    internal sealed class ShellConnectionDisplayProjector
    {
        private static readonly DisplayText EmptyText = GameConnectionDisplayTextProjector.EmptyValueText;

        private readonly DisplayTextRenderer _renderer;

        public ShellConnectionDisplayProjector()
            : this(new DisplayTextRenderer())
        {
        }

        private ShellConnectionDisplayProjector(DisplayTextRenderer renderer)
        {
            ArgumentNullException.ThrowIfNull(renderer);

            _renderer = renderer;
        }

        public ShellConnectionDisplayState Project(GameConnectionSnapshot snapshot)
        {
            var projection = ShellConnectionDisplayProjection.CreateDefault();
            ApplyConnectionStatus(
                projection,
                snapshot.CurrentGame,
                snapshot.ConnectionPhase);
            ApplyEventMonitorStatus(
                projection,
                snapshot.CurrentGame,
                snapshot.ConnectionPhase,
                snapshot.EventMonitorSummary);
            ApplyCommandPresentation(projection, snapshot);

            return Render(projection);
        }

        private static void ApplyConnectionStatus(
            ShellConnectionDisplayProjection projection,
            DetectedGame? detectedGame,
            GameConnectionPhase connectionPhase)
        {
            if (connectionPhase == GameConnectionPhase.NoGame || detectedGame is null)
            {
                projection.DetectedGameText = DisplayText.Resource("NoGameDetected");
                projection.MainStatusText = DisplayText.Resource("GameNotRunning");
                SetConnectionState(projection, detectedGame, ConnectionState.Disconnected, connectionPhase);
                return;
            }

            projection.DetectedGameText = DisplayText.Plain(detectedGame.DisplayName);

            if (connectionPhase == GameConnectionPhase.UnsupportedGame)
            {
                projection.MainStatusText = GameConnectionDisplayTextProjector.FormatUnsupportedStatus(detectedGame);
                SetConnectionState(projection, detectedGame, ConnectionState.Unsupported, connectionPhase);
                return;
            }

            if (connectionPhase == GameConnectionPhase.Disconnecting)
            {
                projection.MainStatusText = DisplayText.Resource("ConnectionStatusDisconnecting");
                SetConnectionState(projection, detectedGame, ConnectionState.Disconnecting, connectionPhase);
                return;
            }

            if (connectionPhase == GameConnectionPhase.Connecting)
            {
                projection.MainStatusText = DisplayText.Resource("ConnectionStatusConnecting");
                SetConnectionState(projection, detectedGame, ConnectionState.Detected, connectionPhase);
                return;
            }

            if (connectionPhase == GameConnectionPhase.Connected)
            {
                projection.MainStatusText = DisplayText.Format(
                    "ConnectedStatusFormat",
                    DisplayText.Plain(detectedGame.DisplayName));
                SetConnectionState(projection, detectedGame, ConnectionState.Connected, connectionPhase);
                return;
            }

            projection.MainStatusText = DisplayText.Format(
                "GameDetectedConnectPromptFormat",
                DisplayText.Plain(detectedGame.DisplayName));
            SetConnectionState(projection, detectedGame, ConnectionState.Detected, connectionPhase);
        }

        private static void ApplyEventMonitorStatus(
            ShellConnectionDisplayProjection projection,
            DetectedGame? detectedGame,
            GameConnectionPhase connectionPhase,
            GameConnectionEventMonitorSummary eventMonitor)
        {
            if (connectionPhase == GameConnectionPhase.Disconnecting
                || eventMonitor.State is GameConnectionEventMonitorState.Disconnecting or GameConnectionEventMonitorState.StopPending)
            {
                projection.MainStatusText = DisplayText.Resource("ConnectionStatusDisconnecting");
                projection.EventMonitorStatusText = DisplayText.Resource("EventMonitorDisconnecting");
                projection.ConnectionLastUpdateText = EmptyText;
                SetConnectionState(
                    projection,
                    detectedGame,
                    ConnectionState.Disconnecting,
                    connectionPhase);
                return;
            }

            if (connectionPhase == GameConnectionPhase.NoGame || detectedGame is null)
            {
                projection.EventMonitorStatusText = DisplayText.Resource("EventMonitorWaitingForMonitor");
                projection.ConnectionLastUpdateText = EmptyText;
                return;
            }

            if (connectionPhase == GameConnectionPhase.UnsupportedGame)
            {
                projection.EventMonitorStatusText = DisplayText.Resource("EventMonitorCaptureDisabled");
                projection.ConnectionLastUpdateText = EmptyText;
                return;
            }

            if (connectionPhase != GameConnectionPhase.Connected)
            {
                projection.EventMonitorStatusText = DisplayText.Resource("EventMonitorWaitingForConnect");
                projection.ConnectionLastUpdateText = EmptyText;
                return;
            }

            projection.ConnectionLastUpdateText = DisplayText.Resource("ConnectionLastUpdateJustNow");
            projection.EventMonitorStatusText = GameConnectionDisplayTextProjector.FormatMonitorStatus(eventMonitor);
            projection.LatestEventStatus = eventMonitor.Status;
        }

        private static void ApplyCommandPresentation(
            ShellConnectionDisplayProjection projection,
            GameConnectionSnapshot snapshot)
        {
            projection.ConnectButtonText = GetConnectButtonText(
                snapshot.CurrentGame,
                snapshot.ConnectionPhase);
            projection.DisconnectButtonText = DisplayText.Resource("DisconnectButtonText");
            projection.IsConnectCommandEnabled = snapshot.ConnectCommandAvailability.IsEnabled;
            projection.IsConnectCommandVisible = snapshot.ConnectCommandAvailability.IsVisible;
            projection.IsDisconnectCommandEnabled = snapshot.DisconnectCommandAvailability.IsEnabled;
            projection.IsDisconnectCommandVisible = snapshot.DisconnectCommandAvailability.IsVisible;
        }

        private static DisplayText GetConnectButtonText(
            DetectedGame? detectedGame,
            GameConnectionPhase connectionPhase)
        {
            if (connectionPhase == GameConnectionPhase.NoGame || detectedGame is null)
            {
                return DisplayText.Resource("ConnectButtonWaitingForGameText");
            }

            if (connectionPhase == GameConnectionPhase.Connecting)
            {
                return DisplayText.Resource("ConnectButtonConnectingText");
            }

            if (connectionPhase == GameConnectionPhase.Disconnecting)
            {
                return DisplayText.Resource("ConnectionCardStatusDisconnecting");
            }

            if (connectionPhase == GameConnectionPhase.Connected)
            {
                return DisplayText.Resource("ConnectButtonConnectedText");
            }

            if (connectionPhase == GameConnectionPhase.UnsupportedGame)
            {
                return DisplayText.Resource("ConnectButtonUnsupportedText");
            }

            return DisplayText.Resource("ConnectButtonText");
        }

        private static void SetConnectionState(
            ShellConnectionDisplayProjection projection,
            DetectedGame? detectedGame,
            ConnectionState connectionState,
            GameConnectionPhase connectionPhase)
        {
            bool isConnecting = connectionPhase == GameConnectionPhase.Connecting;
            bool isDisconnecting = connectionPhase == GameConnectionPhase.Disconnecting;
            UpdateFooterGameStatus(projection, detectedGame);
            UpdateFooterEventStatus(projection, detectedGame, connectionState, isConnecting, isDisconnecting);
            UpdateFooterIndicators(projection, connectionState);
            UpdateConnectionCardStatus(projection, connectionState, isConnecting);
        }

        private static void UpdateFooterGameStatus(
            ShellConnectionDisplayProjection projection,
            DetectedGame? detectedGame)
        {
            projection.FooterGameStatusText = detectedGame is null
                ? DisplayText.Resource("FooterGameNotRunning")
                : DisplayText.Format("FooterGameDetectedFormat", DisplayText.Plain(detectedGame.DisplayName));
        }

        private static void UpdateFooterEventStatus(
            ShellConnectionDisplayProjection projection,
            DetectedGame? detectedGame,
            ConnectionState connectionState,
            bool isConnecting,
            bool isDisconnecting)
        {
            if (connectionState == ConnectionState.Connected)
            {
                projection.FooterEventStatusText = DisplayText.Resource("FooterEventsConnected");
                return;
            }

            if (connectionState == ConnectionState.Disconnecting || isDisconnecting)
            {
                projection.FooterEventStatusText = DisplayText.Resource("FooterEventsDisconnecting");
                return;
            }

            if (isConnecting)
            {
                projection.FooterEventStatusText = DisplayText.Resource("FooterEventsConnecting");
                return;
            }

            if (detectedGame is not null && !detectedGame.IsStatsSupported)
            {
                projection.FooterEventStatusText = DisplayText.Resource("FooterEventsUnsupported");
                return;
            }

            projection.FooterEventStatusText = DisplayText.Resource("FooterEventsNotConnected");
        }

        private static void UpdateFooterIndicators(
            ShellConnectionDisplayProjection projection,
            ConnectionState connectionState)
        {
            projection.IsFooterSuccessIndicatorVisible = connectionState == ConnectionState.Connected;
            projection.IsFooterPendingIndicatorVisible = connectionState is ConnectionState.Detected
                or ConnectionState.Disconnecting
                or ConnectionState.Unsupported;
            projection.IsFooterDisconnectedIndicatorVisible = connectionState == ConnectionState.Disconnected;
            projection.IsFooterErrorIndicatorVisible = false;
        }

        private static void UpdateConnectionCardStatus(
            ShellConnectionDisplayProjection projection,
            ConnectionState connectionState,
            bool isConnecting)
        {
            projection.ConnectionCardStatusText = connectionState switch
            {
                ConnectionState.Connected => DisplayText.Resource("ConnectionCardStatusConnected"),
                ConnectionState.Disconnecting => DisplayText.Resource("ConnectionCardStatusDisconnecting"),
                ConnectionState.Unsupported => DisplayText.Resource("ConnectionCardStatusUnsupported"),
                ConnectionState.Detected when isConnecting => DisplayText.Resource("ConnectionCardStatusConnecting"),
                ConnectionState.Detected => DisplayText.Resource("ConnectionCardStatusMonitoring"),
                _ => DisplayText.Resource("ConnectionCardStatusDisconnected")
            };
        }

        private ShellConnectionDisplayState Render(ShellConnectionDisplayProjection projection)
        {
            return new ShellConnectionDisplayState
            {
                DetectedGameText = _renderer.Render(projection.DetectedGameText),
                EventMonitorStatusText = _renderer.Render(projection.EventMonitorStatusText),
                MainStatusText = _renderer.Render(projection.MainStatusText),
                FooterGameStatusText = _renderer.Render(projection.FooterGameStatusText),
                FooterEventStatusText = _renderer.Render(projection.FooterEventStatusText),
                ConnectionCardStatusText = _renderer.Render(projection.ConnectionCardStatusText),
                ConnectionLastUpdateText = _renderer.Render(projection.ConnectionLastUpdateText),
                ConnectButtonText = _renderer.Render(projection.ConnectButtonText),
                DisconnectButtonText = _renderer.Render(projection.DisconnectButtonText),
                IsConnectCommandEnabled = projection.IsConnectCommandEnabled,
                IsConnectCommandVisible = projection.IsConnectCommandVisible,
                IsDisconnectCommandEnabled = projection.IsDisconnectCommandEnabled,
                IsDisconnectCommandVisible = projection.IsDisconnectCommandVisible,
                IsFooterSuccessIndicatorVisible = projection.IsFooterSuccessIndicatorVisible,
                IsFooterPendingIndicatorVisible = projection.IsFooterPendingIndicatorVisible,
                IsFooterDisconnectedIndicatorVisible = projection.IsFooterDisconnectedIndicatorVisible,
                IsFooterErrorIndicatorVisible = projection.IsFooterErrorIndicatorVisible,
                LatestEventStatus = projection.LatestEventStatus
            };
        }
    }

    internal sealed class ShellConnectionDisplayState
    {
        public const string EmptyText = "--";

        public string DetectedGameText { get; init; } = AppStrings.Get("NoGameDetected");

        public string EventMonitorStatusText { get; init; } = AppStrings.Get("EventMonitorWaitingForMonitor");

        public string MainStatusText { get; init; } = AppStrings.Get("GameNotRunning");

        public string FooterGameStatusText { get; init; } = AppStrings.Get("FooterGameNotRunning");

        public string FooterEventStatusText { get; init; } = AppStrings.Get("FooterEventsNotConnected");

        public string ConnectionCardStatusText { get; init; } = AppStrings.Get("ConnectionCardStatusDisconnected");

        public string ConnectionLastUpdateText { get; init; } = EmptyText;

        public string ConnectButtonText { get; init; } = AppStrings.Get("ConnectButtonText");

        public string DisconnectButtonText { get; init; } = AppStrings.Get("DisconnectButtonText");

        public bool IsConnectCommandEnabled { get; init; }

        public bool IsConnectCommandVisible { get; init; } = true;

        public bool IsDisconnectCommandEnabled { get; init; }

        public bool IsDisconnectCommandVisible { get; init; }

        public bool IsFooterSuccessIndicatorVisible { get; init; }

        public bool IsFooterPendingIndicatorVisible { get; init; }

        public bool IsFooterDisconnectedIndicatorVisible { get; init; } = true;

        public bool IsFooterErrorIndicatorVisible { get; init; }

        public GameEventMonitorStatus LatestEventStatus { get; init; } = GameEventMonitorStatus.WaitingForMonitor;
    }

    internal sealed class ShellConnectionDisplayProjection
    {
        public DisplayText DetectedGameText { get; set; } = DisplayText.Resource("NoGameDetected");

        public DisplayText EventMonitorStatusText { get; set; } = DisplayText.Resource("EventMonitorWaitingForMonitor");

        public DisplayText MainStatusText { get; set; } = DisplayText.Resource("GameNotRunning");

        public DisplayText FooterGameStatusText { get; set; } = DisplayText.Resource("FooterGameNotRunning");

        public DisplayText FooterEventStatusText { get; set; } = DisplayText.Resource("FooterEventsNotConnected");

        public DisplayText ConnectionCardStatusText { get; set; } = DisplayText.Resource("ConnectionCardStatusDisconnected");

        public DisplayText ConnectionLastUpdateText { get; set; } = DisplayText.Plain("--");

        public DisplayText ConnectButtonText { get; set; } = DisplayText.Resource("ConnectButtonText");

        public DisplayText DisconnectButtonText { get; set; } = DisplayText.Resource("DisconnectButtonText");

        public bool IsConnectCommandEnabled { get; set; }

        public bool IsConnectCommandVisible { get; set; } = true;

        public bool IsDisconnectCommandEnabled { get; set; }

        public bool IsDisconnectCommandVisible { get; set; }

        public bool IsFooterSuccessIndicatorVisible { get; set; }

        public bool IsFooterPendingIndicatorVisible { get; set; }

        public bool IsFooterDisconnectedIndicatorVisible { get; set; } = true;

        public bool IsFooterErrorIndicatorVisible { get; set; }

        public GameEventMonitorStatus LatestEventStatus { get; set; } = GameEventMonitorStatus.WaitingForMonitor;

        public static ShellConnectionDisplayProjection CreateDefault()
        {
            return new ShellConnectionDisplayProjection();
        }
    }
}
