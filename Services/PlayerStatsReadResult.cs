using System;

namespace BO2.Services
{
    public sealed record PlayerStatsReadResult(DetectedGame DetectedGame, PlayerStats Stats)
    {
        public DetectedGame DetectedGame { get; init; } =
            DetectedGame ?? throw new ArgumentNullException(nameof(DetectedGame));

        public PlayerStats Stats { get; init; } =
            Stats ?? throw new ArgumentNullException(nameof(Stats));
    }
}
