using System;

namespace BO2.Services
{
    internal sealed class ProcessLifecycleEventArgs(string processName, int processId) : EventArgs
    {
        public string ProcessName { get; } = processName;

        public int ProcessId { get; } = processId;
    }
}
