using System.Linq;

namespace BO2.Services
{
    internal static class GameConnectionDisplayTextProjector
    {
        public static DisplayText EmptyValueText => DisplayText.Plain("--");

        public static DisplayText FormatUnsupportedStatus(DetectedGame detectedGame)
        {
            return string.IsNullOrWhiteSpace(detectedGame.UnsupportedReason)
                ? DisplayText.Format("UnsupportedStatusFormat", DisplayText.Plain(detectedGame.DisplayName))
                : DisplayText.Format(
                    "UnsupportedStatusWithReasonFormat",
                    DisplayText.Plain(detectedGame.DisplayName),
                    DisplayText.Plain(detectedGame.UnsupportedReason));
        }

        public static DisplayText FormatEventCompatibility(GameConnectionEventMonitorState state)
        {
            return state switch
            {
                GameConnectionEventMonitorState.Waiting => DisplayText.Resource("EventMonitorWaitingForMonitor"),
                GameConnectionEventMonitorState.Connecting => DisplayText.Resource("EventMonitorWaitingForMonitor"),
                GameConnectionEventMonitorState.Ready => DisplayText.Resource("EventMonitorCompatible"),
                GameConnectionEventMonitorState.UnsupportedVersion => DisplayText.Resource("EventMonitorUnsupportedVersion"),
                GameConnectionEventMonitorState.CaptureDisabled => DisplayText.Resource("EventMonitorCaptureDisabled"),
                GameConnectionEventMonitorState.PollingFallback => DisplayText.Resource("EventMonitorPollingFallback"),
                _ => DisplayText.Resource("EventMonitorUnknown")
            };
        }

        public static DisplayText FormatMonitorStatus(GameConnectionEventMonitorSummary eventMonitor)
        {
            DisplayText monitorStatusText = FormatEventCompatibility(eventMonitor.State);
            if (eventMonitor.Status.DroppedEventCount > 0 || eventMonitor.Status.DroppedNotifyCount > 0)
            {
                return DisplayText.Format(
                    "EventMonitorCaptureDropsFormat",
                    monitorStatusText,
                    eventMonitor.Status.DroppedEventCount,
                    eventMonitor.Status.DroppedNotifyCount,
                    eventMonitor.Status.PublishedNotifyCount);
            }

            if (eventMonitor.Status.PublishedNotifyCount > 0)
            {
                return DisplayText.Format(
                    "EventMonitorPublishedEventsFormat",
                    monitorStatusText,
                    eventMonitor.Status.PublishedNotifyCount);
            }

            return monitorStatusText;
        }

        public static DisplayText FormatInjectionStatus(GameConnectionEventMonitorSummary eventMonitor)
        {
            if (eventMonitor.State is GameConnectionEventMonitorState.ReadinessFailed or GameConnectionEventMonitorState.LoadingFailed)
            {
                return DisplayText.Plain(eventMonitor.FailureMessage ?? string.Empty);
            }

            return eventMonitor.State switch
            {
                GameConnectionEventMonitorState.Ready => DisplayText.Resource("DllInjectionMonitorReady"),
                GameConnectionEventMonitorState.PollingFallback => DisplayText.Resource("DllInjectionPollingFallback"),
                GameConnectionEventMonitorState.UnsupportedVersion => DisplayText.Resource("DllInjectionUnsupportedVersion"),
                GameConnectionEventMonitorState.CaptureDisabled => DisplayText.Resource("DllInjectionCaptureDisabled"),
                GameConnectionEventMonitorState.Waiting => DisplayText.Resource("DllInjectionWaitingForReadiness"),
                _ => DisplayText.Resource("DllInjectionNotAttempted")
            };
        }

        public static DisplayText FormatCurrentRound(GameConnectionEventMonitorSummary eventMonitor)
        {
            GameEvent? sessionEvent = eventMonitor.Status.RecentEvents
                .LastOrDefault(gameEvent => gameEvent.EventType is GameEventType.StartOfRound or GameEventType.EndOfRound or GameEventType.EndGame);
            if (sessionEvent is null)
            {
                return EmptyValueText;
            }

            if (sessionEvent.EventType == GameEventType.EndGame)
            {
                return DisplayText.Resource("RoundSessionEnded");
            }

            if (sessionEvent.LevelTime <= 0)
            {
                return EmptyValueText;
            }

            return DisplayText.Format(
                "CurrentRoundFormat",
                sessionEvent.LevelTime,
                DisplayText.Plain(sessionEvent.EventName));
        }
    }
}
