using System;
using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameConnectionSessionLifecycleTests
    {
        [Fact]
        public void CreateSnapshot_WhenSupportedSteamZombiesHasNoMonitor_AllowsConnect()
        {
            GameConnectionSessionLifecycle lifecycle = new();
            GameConnectionSessionLifecycleGame detectedGame = CreateSupportedGame(processId: 1001);

            GameConnectionSessionLifecycleSnapshot snapshot = lifecycle.CreateSnapshot(detectedGame);

            Assert.True(snapshot.CanAttemptConnect);
            Assert.False(snapshot.IsConnecting);
            Assert.False(snapshot.IsDisconnecting);
            Assert.False(snapshot.HasInjectionAttemptForCurrentGame);
            Assert.False(snapshot.IsMonitorConnectedForCurrentGame);
        }

        [Fact]
        public void BeginConnect_WhenGameIsMissingOrUnsupported_DoesNotStartConnecting()
        {
            GameConnectionSessionLifecycle lifecycle = new();
            GameConnectionSessionLifecycleGame unsupportedGame = new(
                1001,
                GameVariant.RedactedZombies,
                IsStatsSupported: false);

            bool missingStarted = lifecycle.BeginConnect(null);
            bool unsupportedStarted = lifecycle.BeginConnect(unsupportedGame);

            Assert.False(missingStarted);
            Assert.False(unsupportedStarted);
            Assert.False(lifecycle.CreateSnapshot(null).CanAttemptConnect);
            Assert.False(lifecycle.CreateSnapshot(unsupportedGame).CanAttemptConnect);
            Assert.False(lifecycle.IsConnecting);
        }

        [Fact]
        public void BeginConnect_WhenSupportedSteamZombiesIsEligible_TracksTargetAndBlocksSecondConnect()
        {
            GameConnectionSessionLifecycle lifecycle = new();
            GameConnectionSessionLifecycleGame detectedGame = CreateSupportedGame(processId: 1001);

            bool started = lifecycle.BeginConnect(detectedGame);
            bool secondStarted = lifecycle.BeginConnect(detectedGame);
            GameConnectionSessionLifecycleSnapshot snapshot = lifecycle.CreateSnapshot(detectedGame);

            Assert.True(started);
            Assert.False(secondStarted);
            Assert.True(lifecycle.CanCompleteConnectFor(detectedGame));
            Assert.True(snapshot.IsConnecting);
            Assert.False(snapshot.CanAttemptConnect);
        }

        [Fact]
        public void CompleteConnect_WhenTargetStillMatches_RecordsMonitorOwnership()
        {
            GameConnectionSessionLifecycle lifecycle = new();
            GameConnectionSessionLifecycleGame detectedGame = CreateSupportedGame(processId: 1001);
            GameConnectionSessionLifecycleGame otherGame = CreateSupportedGame(processId: 2002);

            lifecycle.BeginConnect(detectedGame);
            lifecycle.CompleteConnect(
                detectedGame,
                new DllInjectionResult(DllInjectionState.Loaded, "Loaded"),
                new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));
            GameConnectionSessionLifecycleSnapshot snapshot = lifecycle.CreateSnapshot(detectedGame);

            Assert.False(snapshot.IsConnecting);
            Assert.False(snapshot.CanAttemptConnect);
            Assert.True(snapshot.HasInjectionAttemptForCurrentGame);
            Assert.True(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(DllInjectionState.Loaded, snapshot.InjectionResult.State);
            Assert.True(lifecycle.IsMonitorConnectedFor(detectedGame));
            Assert.False(lifecycle.IsMonitorConnectedFor(otherGame));
        }

        [Fact]
        public void ResetMonitorConnectionState_WhenMonitorIsOwned_ReturnsStopRequestAndClearsOwnership()
        {
            GameConnectionSessionLifecycle lifecycle = new();
            GameConnectionSessionLifecycleGame detectedGame = CreateSupportedGame(processId: 1001);
            lifecycle.BeginConnect(detectedGame);
            lifecycle.CompleteConnect(
                detectedGame,
                new DllInjectionResult(DllInjectionState.Loaded, "Loaded"),
                new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));

            GameConnectionSessionMonitorStopRequest stopRequest = lifecycle.ResetMonitorConnectionState();
            GameConnectionSessionLifecycleSnapshot snapshot = lifecycle.CreateSnapshot(detectedGame);

            Assert.True(stopRequest.ShouldRequestStop);
            Assert.Equal(1001, stopRequest.MonitorProcessId);
            Assert.Equal(DllInjectionState.NotAttempted, snapshot.InjectionResult.State);
            Assert.True(snapshot.CanAttemptConnect);
            Assert.False(snapshot.HasInjectionAttemptForCurrentGame);
            Assert.False(snapshot.IsMonitorConnectedForCurrentGame);
        }

        private static GameConnectionSessionLifecycleGame CreateSupportedGame(int processId)
        {
            return new GameConnectionSessionLifecycleGame(
                processId,
                GameVariant.SteamZombies,
                IsStatsSupported: true);
        }
    }
}
