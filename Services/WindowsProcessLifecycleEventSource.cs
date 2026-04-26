using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;

namespace BO2.Services
{
    internal sealed class WindowsProcessLifecycleEventSource : IProcessLifecycleEventSource
    {
        private readonly string _startQuery;
        private readonly string _stopQuery;
        private ManagementEventWatcher? _startWatcher;
        private ManagementEventWatcher? _stopWatcher;

        public WindowsProcessLifecycleEventSource()
            : this(GameProcessDetector.ProcessNames)
        {
        }

        internal WindowsProcessLifecycleEventSource(IEnumerable<string> processNames)
        {
            _startQuery = BuildTraceQuery("Win32_ProcessStartTrace", processNames);
            _stopQuery = BuildTraceQuery("Win32_ProcessStopTrace", processNames);
        }

        public event EventHandler<ProcessLifecycleEventArgs>? ProcessStarted;

        public event EventHandler<ProcessLifecycleEventArgs>? ProcessStopped;

        public void Start()
        {
            _startWatcher ??= CreateWatcher(_startQuery, OnProcessStarted);
            _stopWatcher ??= CreateWatcher(_stopQuery, OnProcessStopped);
            _startWatcher.Start();
            _stopWatcher.Start();
        }

        public void Dispose()
        {
            DisposeWatcher(ref _startWatcher, OnProcessStarted);
            DisposeWatcher(ref _stopWatcher, OnProcessStopped);
        }

        private static ManagementEventWatcher CreateWatcher(
            string query,
            EventArrivedEventHandler eventArrivedHandler)
        {
            ManagementEventWatcher watcher = new(new WqlEventQuery(query));
            watcher.EventArrived += eventArrivedHandler;
            return watcher;
        }

        private static void DisposeWatcher(
            ref ManagementEventWatcher? watcher,
            EventArrivedEventHandler eventArrivedHandler)
        {
            ManagementEventWatcher? existingWatcher = watcher;
            if (existingWatcher is null)
            {
                return;
            }

            existingWatcher.EventArrived -= eventArrivedHandler;
            try
            {
                existingWatcher.Stop();
            }
            catch (ManagementException)
            {
            }

            existingWatcher.Dispose();
            watcher = null;
        }

        private static string BuildTraceQuery(string traceClassName, IEnumerable<string> processNames)
        {
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (string processName in processNames)
            {
                if (string.IsNullOrWhiteSpace(processName))
                {
                    continue;
                }

                string trimmedName = processName.Trim();
                names.Add(trimmedName);
                names.Add(trimmedName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? trimmedName
                    : trimmedName + ".exe");
            }

            List<string> conditions = [];
            foreach (string processName in names)
            {
                conditions.Add("ProcessName = '" + processName.Replace("'", "''", StringComparison.Ordinal) + "'");
            }

            return "SELECT * FROM " + traceClassName + " WHERE " + string.Join(" OR ", conditions);
        }

        private static ProcessLifecycleEventArgs? TryCreateEventArgs(EventArrivedEventArgs args)
        {
            string? processName = args.NewEvent.Properties["ProcessName"]?.Value as string;
            object? processIdValue = args.NewEvent.Properties["ProcessID"]?.Value;
            if (string.IsNullOrWhiteSpace(processName) || processIdValue is null)
            {
                return null;
            }

            int processId = Convert.ToInt32(processIdValue, CultureInfo.InvariantCulture);
            return new ProcessLifecycleEventArgs(processName, processId);
        }

        private void OnProcessStarted(object sender, EventArrivedEventArgs args)
        {
            ProcessLifecycleEventArgs? eventArgs = TryCreateEventArgs(args);
            if (eventArgs is not null)
            {
                ProcessStarted?.Invoke(this, eventArgs);
            }
        }

        private void OnProcessStopped(object sender, EventArrivedEventArgs args)
        {
            ProcessLifecycleEventArgs? eventArgs = TryCreateEventArgs(args);
            if (eventArgs is not null)
            {
                ProcessStopped?.Invoke(this, eventArgs);
            }
        }
    }
}
