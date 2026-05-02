using System;
using System.Globalization;
using System.Linq;

namespace BO2.Services
{
    internal sealed class GameConnectionSessionDisplayRenderer
    {
        public GameConnectionSessionDisplayState Render(GameConnectionSessionDisplayProjection projection)
        {
            ArgumentNullException.ThrowIfNull(projection);

            return new GameConnectionSessionDisplayState
            {
                PointsText = Render(projection.PointsText),
                KillsText = Render(projection.KillsText),
                DownsText = Render(projection.DownsText),
                RevivesText = Render(projection.RevivesText),
                HeadshotsText = Render(projection.HeadshotsText),
                PositionXText = Render(projection.PositionXText),
                PositionYText = Render(projection.PositionYText),
                PositionZText = Render(projection.PositionZText),
                PlayerCandidateDetailsText = Render(projection.PlayerCandidateDetailsText),
                AmmoCandidateDetailsText = Render(projection.AmmoCandidateDetailsText),
                CounterCandidateDetailsText = Render(projection.CounterCandidateDetailsText),
                AddressCandidateDetailsText = Render(projection.AddressCandidateDetailsText),
                DetectedGameText = Render(projection.DetectedGameText),
                EventCompatibilityText = Render(projection.EventCompatibilityText),
                InjectionStatusText = Render(projection.InjectionStatusText),
                EventMonitorStatusText = Render(projection.EventMonitorStatusText),
                CurrentRoundText = Render(projection.CurrentRoundText),
                BoxEventsText = Render(projection.BoxEventsText),
                RecentGameEventsText = Render(projection.RecentGameEventsText),
                StatusText = Render(projection.StatusText),
                GameStatusText = Render(projection.GameStatusText),
                EventConnectionStatusText = Render(projection.EventConnectionStatusText),
                ConnectButtonText = Render(projection.ConnectButtonText),
                ConnectionCardStatusText = Render(projection.ConnectionCardStatusText),
                ConnectionLastUpdateText = Render(projection.ConnectionLastUpdateText),
                IsConnectButtonEnabled = projection.IsConnectButtonEnabled,
                IsConnectButtonVisible = projection.IsConnectButtonVisible,
                IsDisconnectButtonVisible = projection.IsDisconnectButtonVisible,
                IsFooterSuccessStatusVisible = projection.IsFooterSuccessStatusVisible,
                IsFooterPendingStatusVisible = projection.IsFooterPendingStatusVisible,
                IsFooterDisconnectedStatusVisible = projection.IsFooterDisconnectedStatusVisible,
                IsFooterErrorStatusVisible = projection.IsFooterErrorStatusVisible,
                LatestEventStatus = projection.LatestEventStatus
            };
        }

        public string Render(DisplayText text)
        {
            ArgumentNullException.ThrowIfNull(text);

            return text switch
            {
                DisplayText.PlainText plain => plain.Text,
                DisplayText.ResourceText resource => AppStrings.Get(resource.ResourceId),
                DisplayText.FormatText format => AppStrings.Format(
                    format.ResourceId,
                    format.Arguments.Select(RenderArgument).ToArray()),
                DisplayText.LinesText lines => string.Join(
                    Environment.NewLine,
                    lines.Items.Select(Render)),
                DisplayText.IntegerText integer => integer.Value.ToString("N0", CultureInfo.CurrentCulture),
                DisplayText.Float2Text float2 => float2.Value.ToString("N2", CultureInfo.CurrentCulture),
                DisplayText.AddressText address => $"0x{address.Value:X8}",
                DisplayText.LocalTimeText localTime => localTime.Value.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture),
                _ => throw new InvalidOperationException($"Unsupported display text type '{text.GetType().Name}'.")
            };
        }

        private object RenderArgument(object argument)
        {
            return argument is DisplayText text
                ? Render(text)
                : argument;
        }
    }

    internal sealed class GameConnectionSessionDisplayState
    {
        public const string EmptyStatText = "--";

        public string PointsText { get; set; } = EmptyStatText;

        public string KillsText { get; set; } = EmptyStatText;

        public string DownsText { get; set; } = EmptyStatText;

        public string RevivesText { get; set; } = EmptyStatText;

        public string HeadshotsText { get; set; } = EmptyStatText;

        public string PositionXText { get; set; } = EmptyStatText;

        public string PositionYText { get; set; } = EmptyStatText;

        public string PositionZText { get; set; } = EmptyStatText;

        public string PlayerCandidateDetailsText { get; set; } = EmptyStatText;

        public string AmmoCandidateDetailsText { get; set; } = EmptyStatText;

        public string CounterCandidateDetailsText { get; set; } = EmptyStatText;

        public string AddressCandidateDetailsText { get; set; } = EmptyStatText;

        public string DetectedGameText { get; set; } = AppStrings.Get("NoGameDetected");

        public string EventCompatibilityText { get; set; } = AppStrings.Get("NoGameDetected");

        public string InjectionStatusText { get; set; } = AppStrings.Get("DllInjectionNotAttempted");

        public string EventMonitorStatusText { get; set; } = AppStrings.Get("EventMonitorWaitingForMonitor");

        public string CurrentRoundText { get; set; } = EmptyStatText;

        public string BoxEventsText { get; set; } = AppStrings.Get("RecentEventsEmpty");

        public string RecentGameEventsText { get; set; } = AppStrings.Get("RecentEventsEmpty");

        public string StatusText { get; set; } = AppStrings.Get("GameNotRunning");

        public string GameStatusText { get; set; } = AppStrings.Get("FooterGameNotRunning");

        public string EventConnectionStatusText { get; set; } = AppStrings.Get("FooterEventsNotConnected");

        public string ConnectButtonText { get; set; } = AppStrings.Get("ConnectButtonText");

        public string ConnectionCardStatusText { get; set; } = AppStrings.Get("ConnectionCardStatusDisconnected");

        public string ConnectionLastUpdateText { get; set; } = EmptyStatText;

        public bool IsConnectButtonEnabled { get; set; }

        public bool IsConnectButtonVisible { get; set; } = true;

        public bool IsDisconnectButtonVisible { get; set; }

        public bool IsFooterSuccessStatusVisible { get; set; }

        public bool IsFooterPendingStatusVisible { get; set; }

        public bool IsFooterDisconnectedStatusVisible { get; set; } = true;

        public bool IsFooterErrorStatusVisible { get; set; }

        public GameEventMonitorStatus LatestEventStatus { get; set; } = GameEventMonitorStatus.WaitingForMonitor;
    }
}
