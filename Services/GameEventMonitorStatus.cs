using System;
using System.Collections.Generic;

namespace BO2.Services
{
    public sealed record GameEventMonitorStatus(
        GameCompatibilityState CompatibilityState,
        uint DroppedEventCount,
        IReadOnlyList<GameEvent> RecentEvents)
    {
        public static GameEventMonitorStatus WaitingForMonitor { get; } = new(
            GameCompatibilityState.WaitingForMonitor,
            0,
            Array.Empty<GameEvent>());
    }
}
