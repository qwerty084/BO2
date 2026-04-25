using System;
using BO2.Services;
using BO2.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameProcessDetectorTests
    {
        // ── Precedence ──────────────────────────────────────────────────────────────

        [Fact]
        public void WhenNoProcessesDetected_ThenReturnsNull()
        {
            // Arrange
            var fake = new FakeProcessInfoProvider();
            var detector = new GameProcessDetector(fake, TimeProvider.System);

            // Act
            DetectedGame? result = detector.Detect();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void WhenOnlySupportedGameDetected_ThenReturnsSupportedGame()
        {
            // Arrange – SteamZombies (t6zm, no command-line token, has address map)
            var fake = new FakeProcessInfoProvider();
            fake.SetProcessIds("t6zm", 1001);
            var detector = new GameProcessDetector(fake, TimeProvider.System);

            // Act
            DetectedGame? result = detector.Detect();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(GameVariant.SteamZombies, result.Variant);
            Assert.True(result.IsStatsSupported);
        }

        [Fact]
        public void WhenSupportedAndUnsupportedBothDetected_ThenReturnsSupportedGameFirst()
        {
            // Arrange – SteamZombies (supported) + SteamMultiplayer (unsupported, process t6mp)
            var fake = new FakeProcessInfoProvider();
            fake.SetProcessIds("t6zm", 1001);
            fake.SetProcessIds("t6mp", 1002);
            var detector = new GameProcessDetector(fake, TimeProvider.System);

            // Act
            DetectedGame? result = detector.Detect();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(GameVariant.SteamZombies, result.Variant);
        }

        [Fact]
        public void WhenOnlyUnsupportedGamesDetected_ThenReturnsFirstInDefinitionOrder()
        {
            // Arrange – only SteamMultiplayer (t6mp, no token, no address map)
            var fake = new FakeProcessInfoProvider();
            fake.SetProcessIds("t6mp", 2001);
            var detector = new GameProcessDetector(fake, TimeProvider.System);

            // Act
            DetectedGame? result = detector.Detect();

            // Assert – SteamMultiplayer is detected, not supported
            Assert.NotNull(result);
            Assert.Equal(GameVariant.SteamMultiplayer, result.Variant);
            Assert.False(result.IsStatsSupported);
        }

        // ── Command-line token matching ──────────────────────────────────────────────

        [Fact]
        public void WhenDefinitionHasNoCommandLineToken_ProcessAlwaysMatches()
        {
            // Arrange – SteamZombies has null CommandLineToken; no command-line needed
            var fake = new FakeProcessInfoProvider();
            fake.SetProcessIds("t6zm", 1001);
            // Deliberately do NOT register a command line for 1001
            var detector = new GameProcessDetector(fake, TimeProvider.System);

            // Act
            DetectedGame? result = detector.Detect();

            // Assert – matched even without command-line; command line never fetched
            Assert.NotNull(result);
            Assert.Equal(0, fake.CommandLineFetchCount);
        }

        [Fact]
        public void WhenCommandLineContainsToken_MatchesPlutoniumZombies()
        {
            // Arrange – PlutoniumZombies uses process "plutonium-bootstrapper-win32" with token "t6zm"
            var fake = new FakeProcessInfoProvider();
            fake.SetProcessIds("plutonium-bootstrapper-win32", 3001);
            fake.SetCommandLine(3001, "C:\\Plutonium\\bootstrap.exe t6zm");
            var detector = new GameProcessDetector(fake, TimeProvider.System);

            // Act
            DetectedGame? result = detector.Detect();

            // Assert – PlutoniumZombies matches (even though unsupported)
            Assert.NotNull(result);
            Assert.Equal(GameVariant.PlutoniumZombies, result.Variant);
        }

        [Fact]
        public void WhenCommandLineDoesNotContainToken_PlutoniumZombiesNotMatched()
        {
            // Arrange – process present but command line contains "t6mp", not "t6zm"
            var fake = new FakeProcessInfoProvider();
            fake.SetProcessIds("plutonium-bootstrapper-win32", 3001);
            fake.SetCommandLine(3001, "C:\\Plutonium\\bootstrap.exe t6mp");
            var detector = new GameProcessDetector(fake, TimeProvider.System);

            // Act
            DetectedGame? result = detector.Detect();

            // Assert – PlutoniumMultiplayer (token "t6mp") matches; PlutoniumZombies does not
            Assert.NotNull(result);
            Assert.NotEqual(GameVariant.PlutoniumZombies, result.Variant);
        }

        [Fact]
        public void WhenCommandLineTokenMatchIsCaseInsensitive_Matches()
        {
            // Arrange – token "t6zm" in definition; command line has "T6ZM" (upper-case)
            var fake = new FakeProcessInfoProvider();
            fake.SetProcessIds("plutonium-bootstrapper-win32", 3001);
            fake.SetCommandLine(3001, "bootstrap.exe T6ZM");
            var detector = new GameProcessDetector(fake, TimeProvider.System);

            // Act
            DetectedGame? result = detector.Detect();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(GameVariant.PlutoniumZombies, result.Variant);
        }

        // ── Command-line cache ───────────────────────────────────────────────────────

        [Fact]
        public void WhenCacheIsWarm_CommandLineNotFetchedAgain()
        {
            // Arrange – two consecutive Detect() calls with no time advance
            var fake = new FakeProcessInfoProvider();
            fake.SetProcessIds("plutonium-bootstrapper-win32", 3001);
            fake.SetCommandLine(3001, "bootstrap.exe t6zm");
            var timeProvider = new FakeTimeProvider();
            var detector = new GameProcessDetector(fake, timeProvider);

            // Act
            detector.Detect();
            int countAfterFirst = fake.CommandLineFetchCount;
            detector.Detect();
            int countAfterSecond = fake.CommandLineFetchCount;

            // Assert – second Detect() served every token check from cache
            Assert.Equal(countAfterFirst, countAfterSecond);
        }

        [Fact]
        public void WhenCacheEntryExpires_CommandLineFetchedAgain()
        {
            // Arrange
            var fake = new FakeProcessInfoProvider();
            fake.SetProcessIds("plutonium-bootstrapper-win32", 3001);
            fake.SetCommandLine(3001, "bootstrap.exe t6zm");
            var timeProvider = new FakeTimeProvider();
            var detector = new GameProcessDetector(fake, timeProvider);

            // Act – first call populates the cache
            detector.Detect();
            int countAfterFirst = fake.CommandLineFetchCount;

            // Advance past the 5-second cache duration
            timeProvider.Advance(TimeSpan.FromSeconds(6));

            // Second call should re-fetch after expiry
            detector.Detect();
            int countAfterSecond = fake.CommandLineFetchCount;

            // Assert – at least one additional fetch happened
            Assert.True(countAfterSecond > countAfterFirst,
                $"Expected at least one re-fetch after cache expiry, but count went from {countAfterFirst} to {countAfterSecond}.");
        }

        [Fact]
        public void WhenProcessIdChanges_CommandLineFetchedForNewId()
        {
            // Arrange – first Detect() with PID 3001; second with PID 3002 (process restarted)
            var fake = new FakeProcessInfoProvider();
            fake.SetCommandLine(3001, "bootstrap.exe t6zm");
            fake.SetCommandLine(3002, "bootstrap.exe t6zm");
            var timeProvider = new FakeTimeProvider();
            var detector = new GameProcessDetector(fake, timeProvider);

            fake.SetProcessIds("plutonium-bootstrapper-win32", 3001);
            detector.Detect();

            fake.SetProcessIds("plutonium-bootstrapper-win32", 3002);
            detector.Detect();

            // Assert – two distinct fetches, one per PID
            Assert.True(fake.CommandLineFetchCount >= 2,
                "Expected at least two command-line fetches for two distinct process IDs.");
        }
    }
}
