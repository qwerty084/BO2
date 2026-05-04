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
            ApplyStats(projection, snapshot.ConnectionPhase, snapshot.ReadResult);
            ApplyEventMonitorStatus(
                projection,
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
                CurrentRoundText = _renderer.Render(projection.CurrentRoundText),
                BoxEventsText = _renderer.Render(projection.BoxEventsText),
                RecentGameEventsText = _renderer.Render(projection.RecentGameEventsText)
            };
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
            GameConnectionPhase connectionPhase,
            GameConnectionEventMonitorSummary eventMonitor)
        {
            if (connectionPhase == GameConnectionPhase.Disconnecting
                || eventMonitor.State is GameConnectionEventMonitorState.Disconnecting or GameConnectionEventMonitorState.StopPending)
            {
                ClearEvents(projection);
                return;
            }

            if (connectionPhase != GameConnectionPhase.Connected)
            {
                ClearEvents(projection);
                return;
            }

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

        public string CurrentRoundText { get; init; } = EmptyStatText;

        public string BoxEventsText { get; init; } = AppStrings.Get("RecentEventsEmpty");

        public string RecentGameEventsText { get; init; } = AppStrings.Get("RecentEventsEmpty");
    }
}
