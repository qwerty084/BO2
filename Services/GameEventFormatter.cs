using System;
using System.Linq;

namespace BO2.Services
{
    internal static class GameEventFormatter
    {
        public static string FormatRecentBoxEvents(GameEventMonitorStatus eventStatus)
        {
            return FormatRecentBoxEvents(eventStatus, AppStrings.Get("RecentEventsEmpty"));
        }

        public static string FormatBoxTrackerEvents(GameEventMonitorStatus eventStatus)
        {
            return FormatRecentBoxEvents(eventStatus, AppStrings.Get("BoxTrackerEmpty"));
        }

        private static string FormatRecentBoxEvents(GameEventMonitorStatus eventStatus, string emptyText)
        {
            GameEvent[] boxEvents = eventStatus.RecentEvents
                .Where(gameEvent => gameEvent.EventType == GameEventType.BoxEvent)
                .TakeLast(6)
                .ToArray();
            if (boxEvents.Length == 0)
            {
                return emptyText;
            }

            return string.Join(
                Environment.NewLine,
                boxEvents.Select(FormatBoxEvent));
        }

        public static string FormatRecentGameEvents(GameEventMonitorStatus eventStatus)
        {
            GameEvent[] visibleEvents = eventStatus.RecentEvents
                .Where(IsVisibleRecentEvent)
                .TakeLast(6)
                .ToArray();
            if (visibleEvents.Length == 0)
            {
                return AppStrings.Get("RecentEventsEmpty");
            }

            return string.Join(
                Environment.NewLine,
                visibleEvents.Select(gameEvent => AppStrings.Format(
                    "RecentEventFormat",
                    gameEvent.ReceivedAt.ToLocalTime().ToString("HH:mm:ss"),
                    gameEvent.EventType,
                    gameEvent.EventName,
                    gameEvent.LevelTime,
                    gameEvent.OwnerId,
                    gameEvent.StringValue)));
        }

        private static bool IsVisibleRecentEvent(GameEvent gameEvent)
        {
            return gameEvent.EventType is not (
                GameEventType.NotifyCandidateRejected
                or GameEventType.NotifyEntryCandidate
                or GameEventType.StringResolverCandidate
                or GameEventType.NotifyObserved);
        }

        private static string FormatBoxEvent(GameEvent gameEvent)
        {
            string receivedAt = gameEvent.ReceivedAt.ToLocalTime().ToString("HH:mm:ss");
            if (!string.IsNullOrWhiteSpace(gameEvent.WeaponName))
            {
                string weaponDisplayName = WeaponDisplayNameResolver.FormatForEvent(gameEvent.WeaponName);
                return AppStrings.Format(
                    "BoxEventWithWeaponFormat",
                    receivedAt,
                    gameEvent.EventName,
                    weaponDisplayName,
                    gameEvent.OwnerId,
                    gameEvent.StringValue);
            }

            return AppStrings.Format(
                "BoxEventFormat",
                receivedAt,
                gameEvent.EventName,
                gameEvent.OwnerId,
                gameEvent.StringValue);
        }
    }
}
