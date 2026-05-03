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

            return new CurrentGamePageDisplayState
            {
                PointsText = state.PointsText,
                KillsText = state.KillsText,
                DownsText = state.DownsText,
                RevivesText = state.RevivesText,
                HeadshotsText = state.HeadshotsText,
                DetectedGameText = state.DetectedGameText,
                EventCompatibilityText = state.EventCompatibilityText,
                InjectionStatusText = state.InjectionStatusText,
                EventMonitorStatusText = state.EventMonitorStatusText,
                CurrentRoundText = state.CurrentRoundText,
                BoxEventsText = state.BoxEventsText,
                RecentGameEventsText = state.RecentGameEventsText
            };
        }
    }

    internal sealed class CurrentGamePageDisplayState
    {
        public const string EmptyStatText = GameConnectionSessionDisplayState.EmptyStatText;

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
