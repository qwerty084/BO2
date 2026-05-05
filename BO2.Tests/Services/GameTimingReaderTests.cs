using System;
using System.ComponentModel;
using BO2.Services;
using BO2.Tests.Fakes;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameTimingReaderTests
    {
        private const uint ClientActivePointer = 0x2D3ECEB0U;

        [Fact]
        public void WhenSteamZombiesTimingSourceIsActive_ThenReturnsSupportedTimingFacts()
        {
            DetectedGame detectedGame = MakeSteamZombiesGame();
            var memoryAccessor = new FakeProcessMemoryAccessor();
            ConfigureActiveTimingRead(memoryAccessor, gameTimeMilliseconds: 41_600, isPaused: true);
            using var reader = new GameTimingReader(memoryAccessor);

            GameTimingReadResult result = reader.ReadGameTiming(detectedGame);

            Assert.Equal(GameTimingReadStatus.SupportedTiming, result.Status);
            Assert.Equal(TimeSpan.FromMilliseconds(41_600), result.GameTime);
            Assert.True(result.IsPaused);
            Assert.Same(detectedGame, result.DetectedGame);
            Assert.Equal(1, memoryAccessor.AttachCallCount);
        }

        [Fact]
        public void WhenGameVariantHasNoTimingSupport_ThenReturnsUnsupportedWithoutAttaching()
        {
            DetectedGame detectedGame = new(
                GameVariant.RedactedZombies,
                "Redacted Zombies",
                "t6zmv41",
                41,
                null,
                "Unsupported");
            var memoryAccessor = new FakeProcessMemoryAccessor();
            using var reader = new GameTimingReader(memoryAccessor);

            GameTimingReadResult result = reader.ReadGameTiming(detectedGame);

            Assert.Equal(GameTimingReadStatus.UnsupportedTiming, result.Status);
            Assert.Null(result.GameTime);
            Assert.Null(result.IsPaused);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
        }

        [Fact]
        public void WhenTimingSourcePointerIsInvalid_ThenReturnsInvalidTimingSourceState()
        {
            DetectedGame detectedGame = MakeSteamZombiesGame();
            GameTimingAddressMap addressMap = GameTimingAddressMap.SteamZombies;
            var memoryAccessor = new FakeProcessMemoryAccessor();
            memoryAccessor.SetInt32(addressMap.ServerRunningAddress, 1);
            memoryAccessor.SetInt32(addressMap.ClientPausedAddress, 0);
            memoryAccessor.SetInt32(addressMap.ClientActivePointerAddress, 0);
            using var reader = new GameTimingReader(memoryAccessor);

            GameTimingReadResult result = reader.ReadGameTiming(detectedGame);

            Assert.Equal(GameTimingReadStatus.InvalidTimingSourceState, result.Status);
            Assert.Null(result.GameTime);
            Assert.Null(result.IsPaused);
        }

        [Fact]
        public void WhenTimingSourceSnapshotStateIsInvalid_ThenReturnsInvalidTimingSourceState()
        {
            DetectedGame detectedGame = MakeSteamZombiesGame();
            GameTimingAddressMap addressMap = GameTimingAddressMap.SteamZombies;
            var memoryAccessor = new FakeProcessMemoryAccessor();
            memoryAccessor.SetInt32(addressMap.ServerRunningAddress, 1);
            memoryAccessor.SetInt32(addressMap.ClientPausedAddress, 0);
            memoryAccessor.SetInt32(addressMap.ClientActivePointerAddress, (int)ClientActivePointer);
            memoryAccessor.SetInt32(ClientActivePointer + addressMap.SnapshotValidOffset, 0);
            using var reader = new GameTimingReader(memoryAccessor);

            GameTimingReadResult result = reader.ReadGameTiming(detectedGame);

            Assert.Equal(GameTimingReadStatus.InvalidTimingSourceState, result.Status);
            Assert.Null(result.GameTime);
            Assert.Null(result.IsPaused);
        }

        [Fact]
        public void WhenTimingSourceStateIsInactiveLobby_ThenReturnsInactiveLobbyState()
        {
            DetectedGame detectedGame = MakeSteamZombiesGame();
            GameTimingAddressMap addressMap = GameTimingAddressMap.SteamZombies;
            var memoryAccessor = new FakeProcessMemoryAccessor();
            memoryAccessor.SetInt32(addressMap.ServerRunningAddress, 0);
            using var reader = new GameTimingReader(memoryAccessor);

            GameTimingReadResult result = reader.ReadGameTiming(detectedGame);

            Assert.Equal(GameTimingReadStatus.InactiveLobbyState, result.Status);
            Assert.Null(result.GameTime);
            Assert.Null(result.IsPaused);
            Assert.Equal(1, memoryAccessor.AttachCallCount);
        }

        [Fact]
        public void WhenTimingMemoryReadFails_ThenReturnsGenericReadFailureWithoutThrowing()
        {
            DetectedGame detectedGame = MakeSteamZombiesGame();
            GameTimingAddressMap addressMap = GameTimingAddressMap.SteamZombies;
            var memoryAccessor = new FakeProcessMemoryAccessor();
            memoryAccessor.SetInt32Exception(
                addressMap.ServerRunningAddress,
                new Win32Exception(5, "read failed"));
            using var reader = new GameTimingReader(memoryAccessor);

            GameTimingReadResult result = reader.ReadGameTiming(detectedGame);

            Assert.Equal(GameTimingReadStatus.GenericReadFailure, result.Status);
            Assert.Null(result.GameTime);
            Assert.Null(result.IsPaused);
            Assert.Equal(1, memoryAccessor.CloseCallCount);
        }

        [Fact]
        public void WhenTimingSourceReturnsNegativeElapsedTime_ThenReturnsInvalidTimingSourceState()
        {
            DetectedGame detectedGame = MakeSteamZombiesGame();
            var memoryAccessor = new FakeProcessMemoryAccessor();
            ConfigureActiveTimingRead(memoryAccessor, gameTimeMilliseconds: -1, isPaused: false);
            using var reader = new GameTimingReader(memoryAccessor);

            GameTimingReadResult result = reader.ReadGameTiming(detectedGame);

            Assert.Equal(GameTimingReadStatus.InvalidTimingSourceState, result.Status);
            Assert.Null(result.GameTime);
            Assert.Null(result.IsPaused);
        }

        private static DetectedGame MakeSteamZombiesGame()
        {
            return new DetectedGame(
                GameVariant.SteamZombies,
                "Steam Zombies",
                "t6zm",
                1001,
                PlayerStatAddressMap.SteamZombies,
                null);
        }

        private static void ConfigureActiveTimingRead(
            FakeProcessMemoryAccessor memoryAccessor,
            int gameTimeMilliseconds,
            bool isPaused)
        {
            GameTimingAddressMap addressMap = GameTimingAddressMap.SteamZombies;
            memoryAccessor.SetInt32(addressMap.ServerRunningAddress, 1);
            memoryAccessor.SetInt32(addressMap.ClientPausedAddress, isPaused ? 1 : 0);
            memoryAccessor.SetInt32(addressMap.ClientActivePointerAddress, (int)ClientActivePointer);
            memoryAccessor.SetInt32(ClientActivePointer + addressMap.SnapshotValidOffset, 1);
            memoryAccessor.SetInt32(
                ClientActivePointer + addressMap.GameTimeMillisecondsOffset,
                gameTimeMilliseconds);
        }
    }
}
