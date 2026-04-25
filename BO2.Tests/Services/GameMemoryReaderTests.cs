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
        public void WhenNoGameDetected_ThenReturnsDisconnectedResultAndClosesMemoryAccessor()
        {
            var detector = new FakeGameProcessDetector();
            var memoryAccessor = new FakeProcessMemoryAccessor();
            using var reader = new GameMemoryReader(detector, memoryAccessor, TimeProvider.System);

            PlayerStatsReadResult result = reader.ReadPlayerStats();

            Assert.Equal(ConnectionState.Disconnected, result.ConnectionState);
            Assert.Null(result.DetectedGame);
            Assert.Null(result.Stats);
            Assert.Equal(1, memoryAccessor.CloseCallCount);
        }

        [Fact]
        public void WhenUnsupportedGameDetected_ThenReturnsUnsupportedResultWithoutReadingMemory()
        {
            var detector = new FakeGameProcessDetector
            {
                Result = new DetectedGame(
                    GameVariant.RedactedZombies,
                    "Redacted Zombies",
                    "t6zmv41",
                    41,
                    null,
                    "Unsupported")
            };
            var memoryAccessor = new FakeProcessMemoryAccessor();
            using var reader = new GameMemoryReader(detector, memoryAccessor, TimeProvider.System);

            PlayerStatsReadResult result = reader.ReadPlayerStats();

            Assert.Equal(ConnectionState.Unsupported, result.ConnectionState);
            Assert.NotNull(result.DetectedGame);
            Assert.Null(result.Stats);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
            Assert.Equal(1, memoryAccessor.CloseCallCount);
        }

        [Fact]
        public void WhenSupportedGameDetected_ThenReturnsConnectedStats()
        {
            PlayerStatAddressMap addressMap = PlayerStatAddressMap.SteamZombies;
            var detector = new FakeGameProcessDetector
            {
                Result = MakeSupportedGame(addressMap)
            };
            var memoryAccessor = new FakeProcessMemoryAccessor();
            ConfigureRequiredScoreReads(memoryAccessor, addressMap, 1500, 42, 3, 7, 10);
            memoryAccessor.SetSingle(addressMap.DerivedPlayerState.PositionXAddress, 12.5f);
            using var reader = new GameMemoryReader(detector, memoryAccessor, TimeProvider.System);

            PlayerStatsReadResult result = reader.ReadPlayerStats();

            Assert.Equal(ConnectionState.Connected, result.ConnectionState);
            Assert.NotNull(result.Stats);
            Assert.Equal(1500, result.Stats.Points);
            Assert.Equal(42, result.Stats.Kills);
            Assert.Equal(12.5f, result.Stats.Candidates.PositionX);
            Assert.Equal(1, memoryAccessor.AttachCallCount);
        }

        [Fact]
        public void WhenCandidateReadFails_ThenReturnsConnectedResultWithNullCandidate()
        {
            PlayerStatAddressMap addressMap = PlayerStatAddressMap.SteamZombies;
            var detector = new FakeGameProcessDetector
            {
                Result = MakeSupportedGame(addressMap)
            };
            var memoryAccessor = new FakeProcessMemoryAccessor();
            ConfigureRequiredScoreReads(memoryAccessor, addressMap, 100, 2, 0, 1, 1);
            memoryAccessor.SetSingleException(addressMap.DerivedPlayerState.PositionXAddress, new Win32Exception(5, "read failed"));
            using var reader = new GameMemoryReader(detector, memoryAccessor, TimeProvider.System);

            PlayerStatsReadResult result = reader.ReadPlayerStats();

            Assert.Equal(ConnectionState.Connected, result.ConnectionState);
            Assert.NotNull(result.Stats);
            Assert.Null(result.Stats.Candidates.PositionX);
        }

        [Fact]
        public void WhenRequiredReadFails_ThenInvalidatesDetectionCacheAndClosesMemoryAccessor()
        {
            PlayerStatAddressMap addressMap = PlayerStatAddressMap.SteamZombies;
            var detector = new FakeGameProcessDetector
            {
                Result = MakeSupportedGame(addressMap)
            };
            var memoryAccessor = new FakeProcessMemoryAccessor();
            memoryAccessor.SetInt32Exception(addressMap.Scores.PointsAddress, new InvalidOperationException("boom"));
            using var reader = new GameMemoryReader(detector, memoryAccessor, TimeProvider.System);

            Assert.Throws<InvalidOperationException>(() => reader.ReadPlayerStats());

            detector.Result = null;
            PlayerStatsReadResult result = reader.ReadPlayerStats();

            Assert.Equal(ConnectionState.Disconnected, result.ConnectionState);
            Assert.Equal(2, detector.DetectCallCount);
            Assert.True(memoryAccessor.CloseCallCount >= 2);
        }

        [Fact]
        public void WhenAttachGetsInvalidArguments_ThenWrapsAsInvalidOperationAndInvalidatesDetectionCache()
        {
            PlayerStatAddressMap addressMap = PlayerStatAddressMap.SteamZombies;
            var detector = new FakeGameProcessDetector
            {
                Result = MakeSupportedGame(addressMap)
            };
            var memoryAccessor = new FakeProcessMemoryAccessor
            {
                AttachException = new ArgumentOutOfRangeException(nameof(DetectedGame.ProcessId), "bad pid")
            };
            using var reader = new GameMemoryReader(detector, memoryAccessor, TimeProvider.System);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => reader.ReadPlayerStats());

            Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
            detector.Result = null;

            PlayerStatsReadResult result = reader.ReadPlayerStats();

            Assert.Equal(ConnectionState.Disconnected, result.ConnectionState);
            Assert.Equal(2, detector.DetectCallCount);
            Assert.True(memoryAccessor.CloseCallCount >= 2);
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
