using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class PlayerStatsReadResultTests
    {
        // ── GameNotRunning static property ─────────────────────────────────────────

        [Fact]
        public void GameNotRunning_HasDisconnectedConnectionState()
        {
            PlayerStatsReadResult result = PlayerStatsReadResult.GameNotRunning;

            Assert.Equal(ConnectionState.Disconnected, result.ConnectionState);
        }

        [Fact]
        public void GameNotRunning_HasNullStats()
        {
            PlayerStatsReadResult result = PlayerStatsReadResult.GameNotRunning;

            Assert.Null(result.Stats);
        }

        [Fact]
        public void GameNotRunning_HasNullDetectedGame()
        {
            PlayerStatsReadResult result = PlayerStatsReadResult.GameNotRunning;

            Assert.Null(result.DetectedGame);
        }

        [Fact]
        public void GameNotRunning_StatusTextIsNonEmpty()
        {
            PlayerStatsReadResult result = PlayerStatsReadResult.GameNotRunning;

            Assert.False(string.IsNullOrWhiteSpace(result.StatusText));
        }

        // ── Connected result ────────────────────────────────────────────────────────

        [Fact]
        public void WhenConstructedAsConnected_HasConnectedState()
        {
            var game = MakeSteamZombiesGame();
            var stats = MakeStats();
            var result = new PlayerStatsReadResult(game, stats, "Connected: Steam Zombies", ConnectionState.Connected);

            Assert.Equal(ConnectionState.Connected, result.ConnectionState);
        }

        [Fact]
        public void WhenConstructedAsConnected_HasNonNullStats()
        {
            var game = MakeSteamZombiesGame();
            var stats = MakeStats();
            var result = new PlayerStatsReadResult(game, stats, "Connected: Steam Zombies", ConnectionState.Connected);

            Assert.NotNull(result.Stats);
        }

        [Fact]
        public void WhenConstructedAsConnected_StatValuesMatchInput()
        {
            var game = MakeSteamZombiesGame();
            var stats = MakeStats(points: 1500, kills: 42, downs: 3, revives: 7, headshots: 10);
            var result = new PlayerStatsReadResult(game, stats, "Connected: Steam Zombies", ConnectionState.Connected);

            Assert.Equal(1500, result.Stats!.Points);
            Assert.Equal(42, result.Stats.Kills);
            Assert.Equal(3, result.Stats.Downs);
            Assert.Equal(7, result.Stats.Revives);
            Assert.Equal(10, result.Stats.Headshots);
        }

        // ── Unsupported result ──────────────────────────────────────────────────────

        [Fact]
        public void WhenConstructedAsUnsupported_HasUnsupportedState()
        {
            var game = MakeUnsupportedGame();
            var result = new PlayerStatsReadResult(game, null, "Unsupported: Redacted Zombies", ConnectionState.Unsupported);

            Assert.Equal(ConnectionState.Unsupported, result.ConnectionState);
        }

        [Fact]
        public void WhenConstructedAsUnsupported_HasNullStats()
        {
            var game = MakeUnsupportedGame();
            var result = new PlayerStatsReadResult(game, null, "Unsupported: Redacted Zombies", ConnectionState.Unsupported);

            Assert.Null(result.Stats);
        }

        [Fact]
        public void WhenConstructedAsUnsupported_DetectedGameIsPresent()
        {
            var game = MakeUnsupportedGame();
            var result = new PlayerStatsReadResult(game, null, "Unsupported: Redacted Zombies", ConnectionState.Unsupported);

            Assert.NotNull(result.DetectedGame);
            Assert.Equal(GameVariant.RedactedZombies, result.DetectedGame.Variant);
        }

        // ── Disconnected result ─────────────────────────────────────────────────────

        [Fact]
        public void WhenConstructedAsDisconnected_HasDisconnectedState()
        {
            var result = new PlayerStatsReadResult(null, null, "some error", ConnectionState.Disconnected);

            Assert.Equal(ConnectionState.Disconnected, result.ConnectionState);
        }

        // ── DetectedGame.IsStatsSupported ───────────────────────────────────────────

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
