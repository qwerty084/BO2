using System;
using System.ComponentModel;
using BO2.Services;
using BO2.Tests.Fakes;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameMemoryReaderTests
    {
        [Fact]
        public void ClearAttachedGame_ClosesMemoryAccessor()
        {
            var memoryAccessor = new FakeProcessMemoryAccessor();
            using var reader = new GameMemoryReader(memoryAccessor);

            reader.ClearAttachedGame();

            Assert.Equal(1, memoryAccessor.CloseCallCount);
        }

        [Fact]
        public void WhenUnsupportedGameDetected_ThenThrowsAndClosesMemoryAccessor()
        {
            var detectedGame = new DetectedGame(
                GameVariant.RedactedZombies,
                "Redacted Zombies",
                "t6zmv41",
                41,
                null,
                "Unsupported");
            var memoryAccessor = new FakeProcessMemoryAccessor();
            using var reader = new GameMemoryReader(memoryAccessor);

            ArgumentException exception = Assert.Throws<ArgumentException>(() => reader.ReadPlayerStats(detectedGame));

            Assert.Equal("detectedGame", exception.ParamName);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
            Assert.Equal(1, memoryAccessor.CloseCallCount);
        }

        [Fact]
        public void WhenSupportedGameDetected_ThenReturnsConnectedStats()
        {
            PlayerStatAddressMap addressMap = PlayerStatAddressMap.SteamZombies;
            DetectedGame detectedGame = MakeSupportedGame(addressMap);
            var memoryAccessor = new FakeProcessMemoryAccessor();
            ConfigureRequiredScoreReads(memoryAccessor, addressMap, 1500, 42, 3, 7, 10);
            memoryAccessor.SetSingle(addressMap.DerivedPlayerState.PositionXAddress, 12.5f);
            using var reader = new GameMemoryReader(memoryAccessor);

            PlayerStatsReadResult result = reader.ReadPlayerStats(detectedGame);

            Assert.NotNull(result.Stats);
            Assert.Equal(1500, result.Stats.Points);
            Assert.Equal(42, result.Stats.Kills);
            Assert.Equal(12.5f, result.Stats.Candidates.PositionX);
            Assert.Equal(1, memoryAccessor.AttachCallCount);
        }

        [Fact]
        public void ClearAttachedGame_AfterRead_ClosesCurrentMemoryAccessor()
        {
            PlayerStatAddressMap addressMap = PlayerStatAddressMap.SteamZombies;
            DetectedGame detectedGame = MakeSupportedGame(addressMap);
            var memoryAccessor = new FakeProcessMemoryAccessor();
            ConfigureRequiredScoreReads(memoryAccessor, addressMap, 1500, 42, 3, 7, 10);
            using var reader = new GameMemoryReader(memoryAccessor);

            PlayerStatsReadResult result = reader.ReadPlayerStats(detectedGame);
            reader.ClearAttachedGame();

            Assert.NotNull(result.Stats);
            Assert.Equal(1, memoryAccessor.AttachCallCount);
            Assert.True(memoryAccessor.CloseCallCount >= 1);
        }

        [Fact]
        public void WhenCandidateReadFails_ThenReturnsConnectedResultWithNullCandidate()
        {
            PlayerStatAddressMap addressMap = PlayerStatAddressMap.SteamZombies;
            DetectedGame detectedGame = MakeSupportedGame(addressMap);
            var memoryAccessor = new FakeProcessMemoryAccessor();
            ConfigureRequiredScoreReads(memoryAccessor, addressMap, 100, 2, 0, 1, 1);
            memoryAccessor.SetSingleException(addressMap.DerivedPlayerState.PositionXAddress, new Win32Exception(5, "read failed"));
            using var reader = new GameMemoryReader(memoryAccessor);

            PlayerStatsReadResult result = reader.ReadPlayerStats(detectedGame);

            Assert.NotNull(result.Stats);
            Assert.Null(result.Stats.Candidates.PositionX);
        }

        [Fact]
        public void WhenRequiredReadFails_ThenClosesMemoryAccessor()
        {
            PlayerStatAddressMap addressMap = PlayerStatAddressMap.SteamZombies;
            DetectedGame detectedGame = MakeSupportedGame(addressMap);
            var memoryAccessor = new FakeProcessMemoryAccessor();
            memoryAccessor.SetInt32Exception(addressMap.Scores.PointsAddress, new InvalidOperationException("boom"));
            using var reader = new GameMemoryReader(memoryAccessor);

            Assert.Throws<InvalidOperationException>(() => reader.ReadPlayerStats(detectedGame));

            Assert.Equal(1, memoryAccessor.CloseCallCount);
        }

        [Fact]
        public void WhenUnexpectedRequiredReadFails_ThenClosesMemoryAccessor()
        {
            PlayerStatAddressMap addressMap = PlayerStatAddressMap.SteamZombies;
            DetectedGame detectedGame = MakeSupportedGame(addressMap);
            var memoryAccessor = new FakeProcessMemoryAccessor();
            memoryAccessor.SetInt32Exception(addressMap.Scores.PointsAddress, new FormatException("boom"));
            using var reader = new GameMemoryReader(memoryAccessor);

            Assert.Throws<FormatException>(() => reader.ReadPlayerStats(detectedGame));

            Assert.Equal(1, memoryAccessor.CloseCallCount);
        }

        [Fact]
        public void WhenAttachGetsInvalidArguments_ThenWrapsAsInvalidOperationAndClosesMemoryAccessor()
        {
            PlayerStatAddressMap addressMap = PlayerStatAddressMap.SteamZombies;
            DetectedGame detectedGame = MakeSupportedGame(addressMap);
            var memoryAccessor = new FakeProcessMemoryAccessor
            {
                AttachException = new ArgumentOutOfRangeException(nameof(DetectedGame.ProcessId), "bad pid")
            };
            using var reader = new GameMemoryReader(memoryAccessor);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => reader.ReadPlayerStats(detectedGame));

            Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
            Assert.Equal(1, memoryAccessor.CloseCallCount);
        }

        private static DetectedGame MakeSupportedGame(PlayerStatAddressMap addressMap)
        {
            return new DetectedGame(
                GameVariant.SteamZombies,
                "Steam Zombies",
                "t6zm",
                1001,
                addressMap,
                null);
        }

        private static void ConfigureRequiredScoreReads(
            FakeProcessMemoryAccessor memoryAccessor,
            PlayerStatAddressMap addressMap,
            int points,
            int kills,
            int downs,
            int revives,
            int headshots)
        {
            memoryAccessor.SetInt32(addressMap.Scores.PointsAddress, points);
            memoryAccessor.SetInt32(addressMap.Scores.KillsAddress, kills);
            memoryAccessor.SetInt32(addressMap.Scores.DownsAddress, downs);
            memoryAccessor.SetInt32(addressMap.Scores.RevivesAddress, revives);
            memoryAccessor.SetInt32(addressMap.Scores.HeadshotsAddress, headshots);
        }
    }
}
