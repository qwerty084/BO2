using BO2.Services;
using System;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class PlayerStatsReadResultTests
    {
        [Fact]
        public void WhenConstructed_HasDetectedGameAndStats()
        {
            DetectedGame game = MakeSteamZombiesGame();
            PlayerStats stats = MakeStats(points: 1500, kills: 42, downs: 3, revives: 7, headshots: 10);

            var result = new PlayerStatsReadResult(game, stats);

            Assert.Same(game, result.DetectedGame);
            Assert.Same(stats, result.Stats);
            Assert.Equal(1500, result.Stats.Points);
            Assert.Equal(42, result.Stats.Kills);
            Assert.Equal(3, result.Stats.Downs);
            Assert.Equal(7, result.Stats.Revives);
            Assert.Equal(10, result.Stats.Headshots);
        }

        [Fact]
        public void WhenDetectedGameIsNull_ThrowsArgumentNullException()
        {
            PlayerStats stats = MakeStats();

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new PlayerStatsReadResult(null!, stats));

            Assert.Equal("DetectedGame", exception.ParamName);
        }

        [Fact]
        public void WhenStatsIsNull_ThrowsArgumentNullException()
        {
            DetectedGame game = MakeSteamZombiesGame();

            ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
                () => new PlayerStatsReadResult(game, null!));

            Assert.Equal("Stats", exception.ParamName);
        }

        [Fact]
        public void DetectedGame_WhenAddressMapPresent_IsStatsSupportedIsTrue()
        {
            var game = MakeSteamZombiesGame();

            Assert.True(game.IsStatsSupported);
        }

        [Fact]
        public void DetectedGame_WhenNoAddressMap_IsStatsSupportedIsFalse()
        {
            var game = MakeUnsupportedGame();

            Assert.False(game.IsStatsSupported);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────

        private static DetectedGame MakeSteamZombiesGame() =>
            new(GameVariant.SteamZombies, "Steam Zombies", "t6zm", 1001, PlayerStatAddressMap.SteamZombies, null);

        private static DetectedGame MakeUnsupportedGame() =>
            new(GameVariant.RedactedZombies, "Redacted Zombies", "t6zmv41", 2001, null, "No address map");

        private static PlayerStats MakeStats(int points = 0, int kills = 0, int downs = 0, int revives = 0, int headshots = 0)
        {
            PlayerCandidateStats candidates = new(
                null, null, null,
                null, null, null,
                null, null, null,
                null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null,
                null, null, null, null, null);
            return new PlayerStats(points, kills, downs, revives, headshots, candidates);
        }
    }
}
