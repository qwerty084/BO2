namespace BO2.Services
{
    public sealed record PlayerStatsReadResult(
        DetectedGame? DetectedGame,
        PlayerStats? Stats,
        string StatusText,
        ConnectionState ConnectionState)
    {
        public static PlayerStatsReadResult GameNotRunning => new(
            null,
            null,
            AppStrings.Get("GameNotRunning"),
            ConnectionState.Disconnected);
    }
}
