using System;

namespace BO2.Services
{
    public sealed record GameEvent(
        GameEventType EventType,
        string EventName,
        int LevelTime,
        uint OwnerId,
        uint StringValue,
        DateTimeOffset ReceivedAt);
}
