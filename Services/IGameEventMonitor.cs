using System;

namespace BO2.Services
{
    public interface IGameEventMonitor : IDisposable
    {
        GameEventMonitorStatus ReadStatus(DateTimeOffset receivedAt);
    }
}
