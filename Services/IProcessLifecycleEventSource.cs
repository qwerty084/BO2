using System;

namespace BO2.Services
{
    internal interface IProcessLifecycleEventSource : IDisposable
    {
        event EventHandler<ProcessLifecycleEventArgs>? ProcessStarted;

        event EventHandler<ProcessLifecycleEventArgs>? ProcessStopped;

        void Start();
    }
}
