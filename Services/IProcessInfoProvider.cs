namespace BO2.Services
{
    // Seam for injecting fake process/command-line data in unit tests, and for isolating
    // the real Windows Process + WMI calls from the pure detection logic in GameProcessDetector.
    internal interface IProcessInfoProvider
    {
        /// <summary>Returns the IDs of all running processes with the given name.</summary>
        int[] GetProcessIds(string processName);

        /// <summary>Returns the command-line string for the process, or null if unavailable.</summary>
        string? GetCommandLine(int processId);
    }
}
