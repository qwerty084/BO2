using System;

namespace BO2.Services
{
    internal sealed class CurrentGamePageDisplayProjector
    {
        private readonly DisplayTextRenderer _renderer;

        public CurrentGamePageDisplayProjector()
            : this(new DisplayTextRenderer())
        {
        }

        private CurrentGamePageDisplayProjector(DisplayTextRenderer renderer)
        {
            ArgumentNullException.ThrowIfNull(renderer);

            _renderer = renderer;
        }

        public CurrentGamePageDisplayState Project(GameConnectionSnapshot snapshot)
        {
            var projection = CurrentGamePageDisplayProjection.CreateDefault();
            projection.PageStatusText = GetPageStatusText(snapshot.ConnectionPhase);
            ApplyDetectedGame(projection, snapshot.CurrentGame);
            ApplyStats(projection, snapshot.ConnectionPhase, snapshot.ReadResult);
            ApplyEventMonitorStatus(
                projection,
                snapshot.CurrentGame,
                snapshot.ConnectionPhase,
                snapshot.EventMonitorSummary);

            return new CurrentGamePageDisplayState
            {
                PageStatusText = _renderer.Render(projection.PageStatusText),
                PointsText = _renderer.Render(projection.PointsText),
                KillsText = _renderer.Render(projection.KillsText),
                DownsText = _renderer.Render(projection.DownsText),
                RevivesText = _renderer.Render(projection.RevivesText),
                HeadshotsText = _renderer.Render(projection.HeadshotsText),
                DetectedGameText = _renderer.Render(projection.DetectedGameText),
                EventCompatibilityText = _renderer.Render(projection.EventCompatibilityText),
                InjectionStatusText = _renderer.Render(projection.InjectionStatusText),
                EventMonitorStatusText = _renderer.Render(projection.EventMonitorStatusText),
                CurrentRoundText = _renderer.Render(projection.CurrentRoundText),
                BoxEventsText = _renderer.Render(projection.BoxEventsText),
                RecentGameEventsText = _renderer.Render(projection.RecentGameEventsText)
            };
        }

        private static void ApplyDetectedGame(
            CurrentGamePageDisplayProjection projection,
            DetectedGame? currentGame)
        {
            projection.DetectedGameText = currentGame is null
                ? DisplayText.Resource("NoGameDetected")
                : DisplayText.Plain(currentGame.DisplayName);
        }

        private static void ApplyStats(
            CurrentGamePageDisplayProjection projection,
            GameConnectionPhase connectionPhase,
            PlayerStatsReadResult? result)
        {
            if (connectionPhase != GameConnectionPhase.Connected || result?.Stats is null)
            {
                ClearStats(projection);
                return;
            }

            projection.PointsText = DisplayText.Integer(result.Stats.Points);
            projection.KillsText = DisplayText.Integer(result.Stats.Kills);
            projection.DownsText = DisplayText.Integer(result.Stats.Downs);
            projection.RevivesText = DisplayText.Integer(result.Stats.Revives);
            projection.HeadshotsText = DisplayText.Integer(result.Stats.Headshots);
        }

        private static void ApplyEventMonitorStatus(
            CurrentGamePageDisplayProjection projection,
            DetectedGame? detectedGame,
            GameConnectionPhase connectionPhase,
            GameConnectionEventMonitorSummary eventMonitor)
        {
            if (connectionPhase == GameConnectionPhase.Disconnecting
                || eventMonitor.State is GameConnectionEventMonitorState.Disconnecting or GameConnectionEventMonitorState.StopPending)
            {
                projection.InjectionStatusText = DisplayText.Resource("DllInjectionDisconnecting");
                projection.EventMonitorStatusText = DisplayText.Resource("EventMonitorDisconnecting");
                ClearEvents(projection);
                return;
            }

            if (connectionPhase == GameConnectionPhase.NoGame)
            {
                projection.InjectionStatusText = DisplayText.Resource("DllInjectionNotAttempted");
                projection.EventCompatibilityText = DisplayText.Resource("NoGameDetected");
                projection.EventMonitorStatusText = DisplayText.Resource("EventMonitorWaitingForMonitor");
                ClearEvents(projection);
                return;
            }

            if (connectionPhase == GameConnectionPhase.UnsupportedGame && detectedGame is not null)
            {
                projection.InjectionStatusText = DisplayText.Format(
                    "DllInjectionUnsupportedGameFormat",
                    DisplayText.Plain(detectedGame.DisplayName));
                projection.EventCompatibilityText = DisplayText.Format(
                    "EventMonitorUnsupportedGameFormat",
                    DisplayText.Plain(detectedGame.DisplayName));
                projection.EventMonitorStatusText = DisplayText.Resource("EventMonitorCaptureDisabled");
                ClearEvents(projection);
                return;
            }

            if (detectedGame is null)
            {
                projection.InjectionStatusText = DisplayText.Resource("DllInjectionNotAttempted");
                projection.EventCompatibilityText = DisplayText.Resource("NoGameDetected");
                projection.EventMonitorStatusText = DisplayText.Resource("EventMonitorWaitingForMonitor");
                ClearEvents(projection);
                return;
            }

            projection.EventCompatibilityText = DisplayText.Resource("GameProcessDetectorDisplayNameSteamZombies");
            if (connectionPhase is GameConnectionPhase.Detected or GameConnectionPhase.StatsOnlyDetected
                || connectionPhase != GameConnectionPhase.Connected)
            {
                projection.InjectionStatusText = connectionPhase == GameConnectionPhase.Connecting
                    ? DisplayText.Resource("DllInjectionConnecting")
                    : eventMonitor.State is GameConnectionEventMonitorState.ReadinessFailed or GameConnectionEventMonitorState.LoadingFailed
                        ? DisplayText.Plain(eventMonitor.FailureMessage ?? string.Empty)
                        : DisplayText.Resource("DllInjectionWaitingForConnect");
                projection.EventMonitorStatusText = DisplayText.Resource("EventMonitorWaitingForConnect");
                ClearEvents(projection);
                return;
            }

            projection.InjectionStatusText = GameConnectionDisplayTextProjector.FormatInjectionStatus(eventMonitor);
            projection.EventMonitorStatusText = GameConnectionDisplayTextProjector.FormatMonitorStatus(eventMonitor);
            projection.CurrentRoundText = GameConnectionDisplayTextProjector.FormatCurrentRound(eventMonitor);
            projection.BoxEventsText = GameEventDisplayTextProjector.FormatRecentBoxEvents(
                eventMonitor.Status,
                DisplayText.Resource("RecentEventsEmpty"));
            projection.RecentGameEventsText = GameEventDisplayTextProjector.FormatRecentGameEvents(
                eventMonitor.Status,
                DisplayText.Resource("RecentEventsEmpty"));
        }

        private static void ClearStats(CurrentGamePageDisplayProjection projection)
        {
            projection.PointsText = GameConnectionDisplayTextProjector.EmptyValueText;
            projection.KillsText = GameConnectionDisplayTextProjector.EmptyValueText;
            projection.DownsText = GameConnectionDisplayTextProjector.EmptyValueText;
            projection.RevivesText = GameConnectionDisplayTextProjector.EmptyValueText;
            projection.HeadshotsText = GameConnectionDisplayTextProjector.EmptyValueText;
        }

        private static void ClearEvents(CurrentGamePageDisplayProjection projection)
        {
            projection.CurrentRoundText = GameConnectionDisplayTextProjector.EmptyValueText;
            projection.BoxEventsText = DisplayText.Resource("RecentEventsEmpty");
            projection.RecentGameEventsText = DisplayText.Resource("RecentEventsEmpty");
        }

        private static DisplayText GetPageStatusText(GameConnectionPhase connectionPhase)
        {
            return connectionPhase switch
            {
                GameConnectionPhase.Connecting => DisplayText.Resource("CurrentGamePageStatusConnecting"),
                GameConnectionPhase.Connected => DisplayText.Resource("CurrentGamePageStatusConnected"),
                GameConnectionPhase.Disconnecting => DisplayText.Resource("CurrentGamePageStatusDisconnecting"),
                _ => DisplayText.Resource("CurrentGamePageStatusNotConnected")
            };
        }
    }

    internal sealed class CurrentGamePageDisplayProjection
    {
        public DisplayText PageStatusText { get; set; } = DisplayText.Resource("CurrentGamePageStatusNotConnected");

        public DisplayText PointsText { get; set; } = GameConnectionDisplayTextProjector.EmptyValueText;

        public DisplayText KillsText { get; set; } = GameConnectionDisplayTextProjector.EmptyValueText;

        public DisplayText DownsText { get; set; } = GameConnectionDisplayTextProjector.EmptyValueText;

        public DisplayText RevivesText { get; set; } = GameConnectionDisplayTextProjector.EmptyValueText;

        public DisplayText HeadshotsText { get; set; } = GameConnectionDisplayTextProjector.EmptyValueText;

        public DisplayText DetectedGameText { get; set; } = DisplayText.Resource("NoGameDetected");

        public DisplayText EventCompatibilityText { get; set; } = DisplayText.Resource("NoGameDetected");

        public DisplayText InjectionStatusText { get; set; } = DisplayText.Resource("DllInjectionNotAttempted");

        public DisplayText EventMonitorStatusText { get; set; } = DisplayText.Resource("EventMonitorWaitingForMonitor");

        public DisplayText CurrentRoundText { get; set; } = GameConnectionDisplayTextProjector.EmptyValueText;

        public DisplayText BoxEventsText { get; set; } = DisplayText.Resource("RecentEventsEmpty");

        public DisplayText RecentGameEventsText { get; set; } = DisplayText.Resource("RecentEventsEmpty");

        public static CurrentGamePageDisplayProjection CreateDefault()
        {
            return new CurrentGamePageDisplayProjection();
        }
    }

    internal sealed class CurrentGamePageDisplayState
    {
        public const string EmptyStatText = "--";

        public string PageStatusText { get; init; } = AppStrings.Get("CurrentGamePageStatusNotConnected");

        public string PointsText { get; init; } = EmptyStatText;

        public string KillsText { get; init; } = EmptyStatText;

        public string DownsText { get; init; } = EmptyStatText;

        public string RevivesText { get; init; } = EmptyStatText;

        public string HeadshotsText { get; init; } = EmptyStatText;

        public string DetectedGameText { get; init; } = AppStrings.Get("NoGameDetected");

        public string EventCompatibilityText { get; init; } = AppStrings.Get("NoGameDetected");

        public string InjectionStatusText { get; init; } = AppStrings.Get("DllInjectionNotAttempted");

        public string EventMonitorStatusText { get; init; } = AppStrings.Get("EventMonitorWaitingForMonitor");

        public string CurrentRoundText { get; init; } = EmptyStatText;

        public string BoxEventsText { get; init; } = AppStrings.Get("RecentEventsEmpty");

        public string RecentGameEventsText { get; init; } = AppStrings.Get("RecentEventsEmpty");
    }
}
