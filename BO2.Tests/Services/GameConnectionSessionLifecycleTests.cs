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
            bool completed = lifecycle.CompleteConnect(
                detectedGame,
                new DllInjectionResult(DllInjectionState.Loaded, "Loaded"),
                new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));
            GameConnectionSessionLifecycleSnapshot snapshot = lifecycle.CreateSnapshot(detectedGame);

            Assert.True(completed);
            Assert.False(snapshot.IsConnecting);
            Assert.False(snapshot.CanAttemptConnect);
            Assert.True(snapshot.HasInjectionAttemptForCurrentGame);
            Assert.True(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(DllInjectionState.Loaded, snapshot.InjectionResult.State);
            Assert.True(lifecycle.IsMonitorConnectedFor(detectedGame));
            Assert.False(lifecycle.IsMonitorConnectedFor(otherGame));
        }

        [Fact]
        public void CompleteConnect_WhenTargetDoesNotMatch_ClearsConnectWithoutRecordingInjectionResult()
        {
            GameConnectionSessionLifecycle lifecycle = new();
            GameConnectionSessionLifecycleGame connectTarget = CreateSupportedGame(processId: 1001);
            GameConnectionSessionLifecycleGame currentGame = CreateSupportedGame(processId: 2002);

            lifecycle.BeginConnect(connectTarget);
            bool completed = lifecycle.CompleteConnect(
                currentGame,
                new DllInjectionResult(DllInjectionState.Loaded, "Loaded"),
                new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));
            GameConnectionSessionLifecycleSnapshot targetSnapshot = lifecycle.CreateSnapshot(connectTarget);
            GameConnectionSessionLifecycleSnapshot currentSnapshot = lifecycle.CreateSnapshot(currentGame);

            Assert.False(completed);
            Assert.False(targetSnapshot.IsConnecting);
            Assert.Equal(DllInjectionState.NotAttempted, targetSnapshot.InjectionResult.State);
            Assert.False(targetSnapshot.HasInjectionAttemptForCurrentGame);
            Assert.False(targetSnapshot.IsMonitorConnectedForCurrentGame);
            Assert.True(targetSnapshot.CanAttemptConnect);
            Assert.Equal(DllInjectionState.NotAttempted, currentSnapshot.InjectionResult.State);
            Assert.False(currentSnapshot.HasInjectionAttemptForCurrentGame);
            Assert.False(currentSnapshot.IsMonitorConnectedForCurrentGame);
            Assert.True(currentSnapshot.CanAttemptConnect);
        }

        [Fact]
        public void CancelConnect_WhenAnotherMonitorIsOwned_ClearsConnectAndPreservesMonitorOwnership()
        {
            GameConnectionSessionLifecycle lifecycle = new();
            GameConnectionSessionLifecycleGame connectedGame = CreateSupportedGame(processId: 1001);
            GameConnectionSessionLifecycleGame connectTarget = CreateSupportedGame(processId: 2002);
            lifecycle.BeginConnect(connectedGame);
            lifecycle.CompleteConnect(
                connectedGame,
                new DllInjectionResult(DllInjectionState.Loaded, "Loaded"),
                new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));

            bool started = lifecycle.BeginConnect(connectTarget);
            lifecycle.CancelConnect();
            GameConnectionSessionLifecycleSnapshot connectedSnapshot = lifecycle.CreateSnapshot(connectedGame);
            GameConnectionSessionLifecycleSnapshot targetSnapshot = lifecycle.CreateSnapshot(connectTarget);

            Assert.True(started);
            Assert.False(connectedSnapshot.IsConnecting);
            Assert.True(connectedSnapshot.IsMonitorConnectedForCurrentGame);
            Assert.True(connectedSnapshot.HasInjectionAttemptForCurrentGame);
            Assert.Equal(DllInjectionState.Loaded, connectedSnapshot.InjectionResult.State);
            Assert.False(targetSnapshot.IsConnecting);
            Assert.False(targetSnapshot.IsMonitorConnectedForCurrentGame);
            Assert.False(targetSnapshot.HasInjectionAttemptForCurrentGame);
            Assert.True(targetSnapshot.CanAttemptConnect);
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

        [Fact]
        public void ResetForDetectedGameChange_WhenGameIsUnchanged_PreservesMonitorOwnership()
        {
            GameConnectionSessionLifecycle lifecycle = new();
            GameConnectionSessionLifecycleGame detectedGame = CreateSupportedGame(processId: 1001);
            lifecycle.BeginConnect(detectedGame);
            lifecycle.CompleteConnect(
                detectedGame,
                new DllInjectionResult(DllInjectionState.Loaded, "Loaded"),
                new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));

            GameConnectionSessionMonitorStopRequest stopRequest = lifecycle.ResetForDetectedGameChange(
                detectedGame,
                detectedGame);
            GameConnectionSessionLifecycleSnapshot snapshot = lifecycle.CreateSnapshot(detectedGame);

            Assert.False(stopRequest.ShouldRequestStop);
            Assert.Null(stopRequest.MonitorProcessId);
            Assert.Equal(DllInjectionState.Loaded, snapshot.InjectionResult.State);
            Assert.False(snapshot.CanAttemptConnect);
            Assert.True(snapshot.HasInjectionAttemptForCurrentGame);
            Assert.True(snapshot.IsMonitorConnectedForCurrentGame);
        }

        [Fact]
        public void ResetForDetectedGameChange_WhenGameChangesWithoutOwnedMonitor_ClearsTransientStateWithoutStopRequest()
        {
            GameConnectionSessionLifecycle lifecycle = new();
            GameConnectionSessionLifecycleGame originalGame = CreateSupportedGame(processId: 1001);
            GameConnectionSessionLifecycleGame detectedGame = CreateSupportedGame(processId: 2002);
            lifecycle.BeginConnect(originalGame);

            GameConnectionSessionMonitorStopRequest stopRequest = lifecycle.ResetForDetectedGameChange(
                originalGame,
                detectedGame);
            GameConnectionSessionLifecycleSnapshot snapshot = lifecycle.CreateSnapshot(detectedGame);

            Assert.False(stopRequest.ShouldRequestStop);
            Assert.Null(stopRequest.MonitorProcessId);
            Assert.False(snapshot.IsConnecting);
            Assert.True(snapshot.CanAttemptConnect);
            Assert.False(snapshot.HasInjectionAttemptForCurrentGame);
            Assert.False(snapshot.IsMonitorConnectedForCurrentGame);
        }

        [Fact]
        public void ResetForDetectedGameChange_WhenGameChangesWithOwnedMonitor_ReturnsStopRequestAndClearsOwnership()
        {
            GameConnectionSessionLifecycle lifecycle = new();
            GameConnectionSessionLifecycleGame connectedGame = CreateSupportedGame(processId: 1001);
            GameConnectionSessionLifecycleGame detectedGame = CreateSupportedGame(processId: 2002);
            lifecycle.BeginConnect(connectedGame);
            lifecycle.CompleteConnect(
                connectedGame,
                new DllInjectionResult(DllInjectionState.Loaded, "Loaded"),
                new DateTimeOffset(2026, 5, 2, 0, 0, 0, TimeSpan.Zero));

            GameConnectionSessionMonitorStopRequest stopRequest = lifecycle.ResetForDetectedGameChange(
                connectedGame,
                detectedGame);
            GameConnectionSessionLifecycleSnapshot connectedSnapshot = lifecycle.CreateSnapshot(connectedGame);
            GameConnectionSessionLifecycleSnapshot detectedSnapshot = lifecycle.CreateSnapshot(detectedGame);

            Assert.True(stopRequest.ShouldRequestStop);
            Assert.Equal(1001, stopRequest.MonitorProcessId);
            Assert.Equal(DllInjectionState.NotAttempted, connectedSnapshot.InjectionResult.State);
            Assert.False(connectedSnapshot.HasInjectionAttemptForCurrentGame);
            Assert.False(connectedSnapshot.IsMonitorConnectedForCurrentGame);
            Assert.True(detectedSnapshot.CanAttemptConnect);
            Assert.False(detectedSnapshot.HasInjectionAttemptForCurrentGame);
            Assert.False(detectedSnapshot.IsMonitorConnectedForCurrentGame);
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
