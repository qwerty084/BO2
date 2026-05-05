namespace BO2.Services
{
    internal enum GameConnectionPhase
    {
        NoGame,
        UnsupportedGame,
        Detected,
        Connecting,
        Connected,
        Disconnecting
    }
}
