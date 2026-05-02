using System;
using System.Linq;

namespace BO2.Services
{
    internal static class GameEventFormatter
    {
        private static readonly GameConnectionSessionDisplayRenderer Renderer = new();

        public static string FormatRecentBoxEvents(GameEventMonitorStatus eventStatus)
        {
            return Renderer.Render(GameEventDisplayTextProjector.FormatRecentBoxEvents(
                eventStatus,
                DisplayText.Resource("RecentEventsEmpty")));
        }

        public static string FormatBoxTrackerEvents(GameEventMonitorStatus eventStatus)
        {
            return Renderer.Render(GameEventDisplayTextProjector.FormatRecentBoxEvents(
                eventStatus,
                DisplayText.Resource("BoxTrackerEmpty")));
        }

        public static string FormatRecentGameEvents(GameEventMonitorStatus eventStatus)
        {
            return Renderer.Render(GameEventDisplayTextProjector.FormatRecentGameEvents(
                eventStatus,
                DisplayText.Resource("RecentEventsEmpty")));
        }
    }

    internal static class GameEventDisplayTextProjector
    {
        public static DisplayText FormatRecentBoxEvents(
            GameEventMonitorStatus eventStatus,
            DisplayText emptyText)
        {
            GameEvent[] boxEvents = [.. eventStatus.RecentEvents
                .Where(gameEvent => gameEvent.EventType == GameEventType.BoxEvent)
                .TakeLast(6)];
            if (boxEvents.Length == 0)
            {
                return emptyText;
            }

            return DisplayText.Lines([.. boxEvents.Select(FormatBoxEvent)]);
        }

        public static DisplayText FormatRecentGameEvents(
            GameEventMonitorStatus eventStatus,
            DisplayText emptyText)
        {
            GameEvent[] visibleEvents = [.. eventStatus.RecentEvents
                .Where(IsVisibleRecentEvent)
                .TakeLast(6)];
            if (visibleEvents.Length == 0)
            {
                return emptyText;
            }

            return DisplayText.Lines([.. visibleEvents.Select(FormatRecentGameEvent)]);
        }

        private static bool IsVisibleRecentEvent(GameEvent gameEvent)
        {
            return gameEvent.EventType is not (
                GameEventType.NotifyCandidateRejected
                or GameEventType.NotifyEntryCandidate
                or GameEventType.StringResolverCandidate
                or GameEventType.NotifyObserved);
        }

        private static DisplayText FormatRecentGameEvent(GameEvent gameEvent)
        {
            return DisplayText.Format(
                "RecentEventFormat",
                DisplayText.LocalTime(gameEvent.ReceivedAt),
                gameEvent.EventType,
                gameEvent.EventName,
                gameEvent.LevelTime,
                gameEvent.OwnerId,
                gameEvent.StringValue);
        }

        private static DisplayText FormatBoxEvent(GameEvent gameEvent)
        {
            if (!string.IsNullOrWhiteSpace(gameEvent.WeaponName))
            {
                string weaponDisplayName = WeaponDisplayNameResolver.FormatForEvent(gameEvent.WeaponName);
                return DisplayText.Format(
                    "BoxEventWithWeaponFormat",
                    DisplayText.LocalTime(gameEvent.ReceivedAt),
                    gameEvent.EventName,
                    weaponDisplayName,
                    gameEvent.OwnerId,
                    gameEvent.StringValue);
            }

            return DisplayText.Format(
                "BoxEventFormat",
                DisplayText.LocalTime(gameEvent.ReceivedAt),
                gameEvent.EventName,
                gameEvent.OwnerId,
                gameEvent.StringValue);
        }
    }
}
