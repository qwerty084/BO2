namespace BO2.Services
{
    public sealed record PlayerStatsReadResult(
        DetectedGame? DetectedGame,
        PlayerStats? Stats,
        string StatusText,
        ConnectionState ConnectionState)
    {
        public static PlayerStatsReadResult GameNotRunning { get; } = new(
            null,
            null,
            "Game not running",
            ConnectionState.Disconnected);
    }
}
