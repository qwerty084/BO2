using System;

namespace BO2.Services
{
    public interface IGameEventMonitor : IDisposable
    {
        GameEventMonitorStatus ReadStatus(DateTimeOffset receivedAt, int? targetProcessId);

        void RequestStop(int? targetProcessId);

        bool IsStopComplete(int targetProcessId);
    }
}
