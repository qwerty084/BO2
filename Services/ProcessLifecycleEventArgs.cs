using System;

namespace BO2.Services
{
    internal sealed class ProcessLifecycleEventArgs : EventArgs
    {
        public ProcessLifecycleEventArgs(string processName, int processId)
        {
            ProcessName = processName;
            ProcessId = processId;
        }

        public string ProcessName { get; }

        public int ProcessId { get; }
    }
}
