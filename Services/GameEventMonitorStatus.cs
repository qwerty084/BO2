using System;
using System.Collections.Generic;

namespace BO2.Services
{
    public sealed record GameEventMonitorStatus(
        GameCompatibilityState CompatibilityState,
        uint DroppedEventCount,
        uint DroppedNotifyCount,
        uint PublishedNotifyCount,
        IReadOnlyList<GameEvent> RecentEvents)
    {
        public static GameEventMonitorStatus WaitingForMonitor { get; } = new(
            GameCompatibilityState.WaitingForMonitor,
            0,
            0,
            0,
            []);
    }
}
