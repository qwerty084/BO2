using System;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;

namespace BO2.Services
{
    // Real Windows implementation: wraps Process.GetProcessesByName and WMI command-line queries.
    // Isolated here so GameProcessDetector can be tested with a fake in unit tests.
    internal sealed class WindowsProcessInfoProvider : IProcessInfoProvider
    {
        public int[] GetProcessIds(string processName)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            int[] ids = new int[processes.Length];

            for (int i = 0; i < processes.Length; i++)
            {
                ids[i] = processes[i].Id;
                processes[i].Dispose();
            }

            return ids;
        }

        public string? GetCommandLine(int processId)
        {
            if (processId <= 0)
            {
                return null;
            }

            string query = $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId.ToString(CultureInfo.InvariantCulture)}";

            try
            {
                using ManagementObjectSearcher searcher = new(query);
                using ManagementObjectCollection results = searcher.Get();

                foreach (ManagementBaseObject result in results)
                {
                    using (result)
                    {
                        return result["CommandLine"] as string;
                    }
                }
            }
            catch (ManagementException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
            catch (COMException)
            {
                return null;
            }

            return null;
        }
    }
}
