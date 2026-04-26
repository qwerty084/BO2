namespace BO2.Services
{
    public enum GameCompatibilityState
    {
        Unknown = 0,
        WaitingForMonitor,
        Compatible,
        UnsupportedVersion,
        CaptureDisabled,
        PollingFallback
    }
}
