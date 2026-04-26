using System;

namespace BO2.Services
{
    public sealed record GameEvent(
        GameEventType EventType,
        string EventName,
        int LevelTime,
        DateTimeOffset ReceivedAt);
}
