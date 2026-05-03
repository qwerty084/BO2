using System;

namespace BO2.Services
{
    internal sealed class CurrentGamePageDisplayProjector
    {
        private readonly GameConnectionSessionDisplayProjector _sessionDisplayProjector;
        private readonly GameConnectionSessionDisplayRenderer _sessionDisplayRenderer;

        public CurrentGamePageDisplayProjector()
            : this(
                new GameConnectionSessionDisplayProjector(),
                new GameConnectionSessionDisplayRenderer())
        {
        }

        private CurrentGamePageDisplayProjector(
            GameConnectionSessionDisplayProjector sessionDisplayProjector,
            GameConnectionSessionDisplayRenderer sessionDisplayRenderer)
        {
            ArgumentNullException.ThrowIfNull(sessionDisplayProjector);
            ArgumentNullException.ThrowIfNull(sessionDisplayRenderer);

            _sessionDisplayProjector = sessionDisplayProjector;
            _sessionDisplayRenderer = sessionDisplayRenderer;
        }

        public CurrentGamePageDisplayState Project(GameConnectionSnapshot snapshot)
        {
            GameConnectionSessionDisplayState state = _sessionDisplayRenderer.Render(
                _sessionDisplayProjector.Project(snapshot));
            bool isLive = snapshot.ConnectionPhase == GameConnectionPhase.Connected;

            return new CurrentGamePageDisplayState
            {
                PageStatusText = GetPageStatusText(snapshot.ConnectionPhase),
                PointsText = isLive ? state.PointsText : CurrentGamePageDisplayState.EmptyStatText,
                KillsText = isLive ? state.KillsText : CurrentGamePageDisplayState.EmptyStatText,
                DownsText = isLive ? state.DownsText : CurrentGamePageDisplayState.EmptyStatText,
                RevivesText = isLive ? state.RevivesText : CurrentGamePageDisplayState.EmptyStatText,
                HeadshotsText = isLive ? state.HeadshotsText : CurrentGamePageDisplayState.EmptyStatText,
                DetectedGameText = state.DetectedGameText,
                EventCompatibilityText = state.EventCompatibilityText,
                InjectionStatusText = state.InjectionStatusText,
                EventMonitorStatusText = state.EventMonitorStatusText,
                CurrentRoundText = isLive ? state.CurrentRoundText : CurrentGamePageDisplayState.EmptyStatText,
                BoxEventsText = isLive ? state.BoxEventsText : AppStrings.Get("RecentEventsEmpty"),
                RecentGameEventsText = isLive ? state.RecentGameEventsText : AppStrings.Get("RecentEventsEmpty")
            };
        }

        private static string GetPageStatusText(GameConnectionPhase connectionPhase)
        {
            return connectionPhase switch
            {
                GameConnectionPhase.Connecting => AppStrings.Get("CurrentGamePageStatusConnecting"),
                GameConnectionPhase.Connected => AppStrings.Get("CurrentGamePageStatusConnected"),
                GameConnectionPhase.Disconnecting => AppStrings.Get("CurrentGamePageStatusDisconnecting"),
                _ => AppStrings.Get("CurrentGamePageStatusNotConnected")
            };
        }
    }

    internal sealed class CurrentGamePageDisplayState
    {
        public const string EmptyStatText = GameConnectionSessionDisplayState.EmptyStatText;

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
