namespace BO2.Services
{
    internal enum GameConnectionPhase
    {
        NoGame,
        UnsupportedGame,
        Detected,
        StatsOnlyDetected,
        Connecting,
        Connected,
        Disconnecting
    }
}
