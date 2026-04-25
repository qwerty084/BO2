using System.Collections.Generic;
using BO2.Services;

namespace BO2.Tests.Fakes
{
    /// <summary>
    /// Configurable fake for <see cref="IProcessInfoProvider"/> that lets tests control which
    /// process IDs exist for a given name and what command line each process reports, without
    /// touching the real Windows Process API or WMI.
    /// </summary>
    internal sealed class FakeProcessInfoProvider : IProcessInfoProvider
    {
        private readonly Dictionary<string, int[]> _processIdsByName = [];
        private readonly Dictionary<int, string?> _commandLineByProcessId = [];
        private int _commandLineFetchCount;

        /// <summary>Number of times <see cref="GetCommandLine"/> was called.</summary>
        public int CommandLineFetchCount => _commandLineFetchCount;

        /// <summary>Register process IDs that should appear for a given process name.</summary>
        public void SetProcessIds(string processName, params int[] ids)
        {
            _processIdsByName[processName] = ids;
        }

        /// <summary>Register the command line string returned for a given process ID.</summary>
        public void SetCommandLine(int processId, string? commandLine)
        {
            _commandLineByProcessId[processId] = commandLine;
        }

        public int[] GetProcessIds(string processName)
        {
            return _processIdsByName.TryGetValue(processName, out int[]? ids) ? ids : [];
        }

        public string? GetCommandLine(int processId)
        {
            _commandLineFetchCount++;
            return _commandLineByProcessId.TryGetValue(processId, out string? cmdLine) ? cmdLine : null;
        }
    }
}
