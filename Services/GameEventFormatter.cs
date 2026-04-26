using System;
using System.Linq;

namespace BO2.Services
{
    internal static class GameEventFormatter
    {
        public static string FormatRecentBoxEvents(GameEventMonitorStatus eventStatus)
        {
            GameEvent[] boxEvents = eventStatus.RecentEvents
                .Where(gameEvent => gameEvent.EventType == GameEventType.BoxEvent)
                .TakeLast(6)
                .ToArray();
            if (boxEvents.Length == 0)
            {
                return AppStrings.Get("RecentEventsEmpty");
            }

            return string.Join(
                Environment.NewLine,
                boxEvents.Select(FormatBoxEvent));
        }

        private static string FormatBoxEvent(GameEvent gameEvent)
        {
            string receivedAt = gameEvent.ReceivedAt.ToLocalTime().ToString("HH:mm:ss");
            if (!string.IsNullOrWhiteSpace(gameEvent.WeaponName))
            {
                return AppStrings.Format(
                    "BoxEventWithWeaponFormat",
                    receivedAt,
                    gameEvent.EventName,
                    gameEvent.WeaponName,
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
