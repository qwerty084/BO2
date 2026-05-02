using System;
using System.Collections.Generic;
using BO2.Services;
using BO2.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameConnectionSessionTests
    {
        [Fact]
        public void Snapshot_WhenSessionStartsWithCurrentGame_ExposesInitialStatusState()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            GameConnectionSession session = CreateStartedSession(
                eventMonitor,
                detectedGame,
                memoryAccessor: memoryAccessor);

            GameConnectionSnapshot snapshot = session.Snapshot;

            Assert.Same(detectedGame, snapshot.CurrentGame);
            Assert.Same(detectedGame, snapshot.ReadResult.DetectedGame);
            Assert.Null(snapshot.ReadResult.Stats);
            Assert.Equal(ConnectionState.Detected, snapshot.ReadResult.ConnectionState);
            Assert.Equal(GameCompatibilityState.WaitingForMonitor, snapshot.EventStatus.CompatibilityState);
            Assert.Equal(DllInjectionState.NotAttempted, snapshot.InjectionResult.State);
            Assert.False(snapshot.IsConnecting);
            Assert.False(snapshot.IsDisconnecting);
            Assert.True(snapshot.CanAttemptConnect);
            Assert.False(snapshot.HasInjectionAttemptForCurrentGame);
            Assert.False(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Read_WhenStatsChange_PublishesSnapshotChangedAndUpdatesCurrentSnapshot()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            GameConnectionSession session = CreateStartedSession(
                eventMonitor,
                detectedGame,
                memoryAccessor: memoryAccessor);
            GameConnectionSnapshot initialSnapshot = session.Snapshot;
            List<GameConnectionSnapshotChangedEventArgs> changes = new();
            session.SnapshotChanged += (_, args) => changes.Add(args);

            GameConnectionSnapshot readSnapshot = session.Read();

            GameConnectionSnapshotChangedEventArgs change = Assert.Single(changes);
            Assert.Equal(initialSnapshot, change.PreviousSnapshot);
            Assert.Same(readSnapshot.CurrentGame, change.Snapshot.CurrentGame);
            Assert.Same(readSnapshot.ReadResult, change.Snapshot.ReadResult);
            Assert.NotNull(change.Snapshot.ReadResult.Stats);
            Assert.Equal(readSnapshot.EventStatus, change.Snapshot.EventStatus);
            Assert.Equal(readSnapshot.InjectionResult, change.Snapshot.InjectionResult);
            Assert.Equal(readSnapshot.CanAttemptConnect, change.Snapshot.CanAttemptConnect);
            Assert.Equal(change.Snapshot, session.Snapshot);
            Assert.Equal(1, memoryAccessor.AttachCallCount);
        }

        [Fact]
        public void Read_WhenNoCurrentGame_ReturnsNoGameSnapshot()
        {
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            GameConnectionSession session = CreateStartedSession(eventMonitor);

            GameConnectionSnapshot snapshot = session.Read();

            Assert.Null(snapshot.CurrentGame);
            Assert.Null(snapshot.ReadResult.DetectedGame);
            Assert.Equal(ConnectionState.Disconnected, snapshot.ReadResult.ConnectionState);
            Assert.Equal(GameCompatibilityState.WaitingForMonitor, snapshot.EventStatus.CompatibilityState);
            Assert.False(snapshot.CanAttemptConnect);
            Assert.False(snapshot.HasInjectionAttemptForCurrentGame);
            Assert.False(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(snapshot, session.Snapshot);
        }

        [Fact]
        public void Read_WhenCurrentGameSupported_ReturnsStatsSnapshotWithoutEventMonitor()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            GameConnectionSession session = CreateStartedSession(
                eventMonitor,
                detectedGame,
                memoryAccessor: memoryAccessor);

            GameConnectionSnapshot snapshot = session.Read();

            Assert.Same(detectedGame, snapshot.CurrentGame);
            Assert.Same(detectedGame, snapshot.ReadResult.DetectedGame);
            Assert.NotNull(snapshot.ReadResult.Stats);
            Assert.Equal(ConnectionState.Connected, snapshot.ReadResult.ConnectionState);
            Assert.Equal(GameCompatibilityState.WaitingForMonitor, snapshot.EventStatus.CompatibilityState);
            Assert.True(snapshot.CanAttemptConnect);
            Assert.False(snapshot.HasInjectionAttemptForCurrentGame);
            Assert.False(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(1, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(snapshot, session.Snapshot);
        }

        [Fact]
        public void Read_WhenCurrentGameUnsupported_ReturnsUnsupportedSnapshot()
        {
            DetectedGame detectedGame = CreateUnsupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            GameConnectionSession session = CreateStartedSession(eventMonitor, detectedGame);

            GameConnectionSnapshot snapshot = session.Read();

            Assert.Same(detectedGame, snapshot.CurrentGame);
            Assert.Same(detectedGame, snapshot.ReadResult.DetectedGame);
            Assert.Null(snapshot.ReadResult.Stats);
            Assert.Equal(ConnectionState.Unsupported, snapshot.ReadResult.ConnectionState);
            Assert.Equal(GameCompatibilityState.WaitingForMonitor, snapshot.EventStatus.CompatibilityState);
            Assert.False(snapshot.CanAttemptConnect);
            Assert.False(snapshot.HasInjectionAttemptForCurrentGame);
            Assert.False(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(snapshot, session.Snapshot);
        }

        [Fact]
        public void Read_WhenPollingFallbackIsActive_UpdatesCurrentGameBeforeReadingStats()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            FakeProcessLifecycleEventSource lifecycleEventSource = new()
            {
                StartException = new UnauthorizedAccessException("Process events unavailable.")
            };
            SessionContext context = CreateSessionContext(
                eventMonitor,
                pollingDetectedGame: detectedGame,
                memoryAccessor: memoryAccessor,
                lifecycleEventSource: lifecycleEventSource);
            context.Session.Start();

            GameConnectionSnapshot snapshot = context.Session.Read();

            Assert.True(context.Session.UsesPollingProcessDetection);
            Assert.Same(detectedGame, snapshot.CurrentGame);
            Assert.Same(detectedGame, snapshot.ReadResult.DetectedGame);
            Assert.NotNull(snapshot.ReadResult.Stats);
            Assert.Equal(1, context.PollingProcessDetector.DetectCallCount);
            Assert.Equal(1, memoryAccessor.AttachCallCount);
            Assert.True(snapshot.CanAttemptConnect);
            Assert.Equal(snapshot, context.Session.Snapshot);
        }

        [Fact]
        public void GetStatusSnapshot_WhenCurrentGameChanges_ReturnsCurrentStatusWithoutPlayerStatsRead()
        {
            DetectedGame originalGame = CreateSupportedGame(processId: 1001);
            DetectedGame detectedGame = CreateSupportedGame(processId: 2002);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            SessionContext context = CreateSessionContext(
                eventMonitor,
                detectedGame: originalGame,
                memoryAccessor: memoryAccessor);
            context.Session.Start();
            context.EventDetector.Result = detectedGame;
            context.LifecycleEventSource.RaiseStarted(detectedGame.ProcessName, detectedGame.ProcessId);

            GameConnectionSnapshot snapshot = context.Session.GetStatusSnapshot();

            Assert.Same(detectedGame, snapshot.CurrentGame);
            Assert.Same(detectedGame, snapshot.ReadResult.DetectedGame);
            Assert.Null(snapshot.ReadResult.Stats);
            Assert.Equal(ConnectionState.Detected, snapshot.ReadResult.ConnectionState);
            Assert.True(snapshot.CanAttemptConnect);
            Assert.False(snapshot.IsConnecting);
            Assert.False(snapshot.IsDisconnecting);
            Assert.Equal(GameCompatibilityState.WaitingForMonitor, snapshot.EventStatus.CompatibilityState);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(0, eventMonitor.RequestStopCallCount);
            Assert.Equal(snapshot, context.Session.Snapshot);
        }

        [Fact]
        public void GetStatusSnapshot_WhenConnectIsPending_ReturnsConnectingStatusWithoutPlayerStatsRead()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            GameConnectionSession session = CreateStartedSession(
                eventMonitor,
                detectedGame,
                memoryAccessor: memoryAccessor);

            GameConnectionRefreshResult connectingSnapshot = session.BeginConnect();
            GameConnectionSnapshot statusSnapshot = session.GetStatusSnapshot();

            Assert.True(connectingSnapshot.IsConnecting);
            Assert.True(statusSnapshot.IsConnecting);
            Assert.False(statusSnapshot.CanAttemptConnect);
            Assert.Same(detectedGame, statusSnapshot.CurrentGame);
            Assert.Same(detectedGame, statusSnapshot.ReadResult.DetectedGame);
            Assert.Null(statusSnapshot.ReadResult.Stats);
            Assert.Equal(ConnectionState.Detected, statusSnapshot.ReadResult.ConnectionState);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void GetStatusSnapshot_WhenDisconnecting_ReturnsDisconnectingStatusWithoutPlayerStatsRead()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            GameConnectionSession session = CreateStartedSession(
                eventMonitor,
                detectedGame,
                memoryAccessor: memoryAccessor);
            CompleteConnectWithLoadedMonitor(session);
            session.BeginDisconnect();
            int attachCallCount = memoryAccessor.AttachCallCount;
            eventMonitor.ResetCalls();

            GameConnectionSnapshot statusSnapshot = session.GetStatusSnapshot();

            Assert.True(statusSnapshot.IsDisconnecting);
            Assert.False(statusSnapshot.CanAttemptConnect);
            Assert.True(statusSnapshot.IsMonitorConnectedForCurrentGame);
            Assert.Same(detectedGame, statusSnapshot.CurrentGame);
            Assert.Same(detectedGame, statusSnapshot.ReadResult.DetectedGame);
            Assert.Null(statusSnapshot.ReadResult.Stats);
            Assert.Equal(ConnectionState.Disconnecting, statusSnapshot.ReadResult.ConnectionState);
            Assert.Equal(GameCompatibilityState.WaitingForMonitor, statusSnapshot.EventStatus.CompatibilityState);
            Assert.Equal(attachCallCount, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(0, eventMonitor.IsStopCompleteCallCount);
        }

        [Fact]
        public void HandleReadFailure_WhenConnectIsPending_ClearsTransientStateAndReturnsStatusSnapshot()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            GameConnectionSession session = CreateStartedSession(
                eventMonitor,
                detectedGame,
                memoryAccessor: memoryAccessor);
            eventMonitor.ResetCalls();

            GameConnectionRefreshResult connectingSnapshot = session.BeginConnect();
            GameConnectionSnapshot cleanupSnapshot = session.HandleReadFailure();

            Assert.True(connectingSnapshot.IsConnecting);
            Assert.False(cleanupSnapshot.IsConnecting);
            Assert.False(cleanupSnapshot.IsDisconnecting);
            Assert.True(cleanupSnapshot.CanAttemptConnect);
            Assert.False(cleanupSnapshot.IsMonitorConnectedForCurrentGame);
            Assert.Same(detectedGame, cleanupSnapshot.CurrentGame);
            Assert.Same(detectedGame, cleanupSnapshot.ReadResult.DetectedGame);
            Assert.Null(cleanupSnapshot.ReadResult.Stats);
            Assert.Equal(ConnectionState.Detected, cleanupSnapshot.ReadResult.ConnectionState);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(0, eventMonitor.RequestStopCallCount);
        }

        [Fact]
        public void Read_WhenStatsReadFails_ClearsTransientStateAndPublishesRecoverableSnapshot()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new()
            {
                AttachException = new InvalidOperationException("read failed")
            };
            GameConnectionSession session = CreateStartedSession(
                eventMonitor,
                detectedGame,
                memoryAccessor: memoryAccessor);
            GameConnectionRefreshResult connectingSnapshot = session.BeginConnect();
            List<GameConnectionSnapshotChangedEventArgs> changes = new();
            session.SnapshotChanged += (_, args) => changes.Add(args);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => session.Read());

            GameConnectionSnapshot recoverableSnapshot = session.Snapshot;
            GameConnectionSnapshotChangedEventArgs change = Assert.Single(changes);
            Assert.Equal("read failed", exception.Message);
            Assert.True(connectingSnapshot.IsConnecting);
            Assert.Equal(recoverableSnapshot, change.Snapshot);
            Assert.False(recoverableSnapshot.IsConnecting);
            Assert.False(recoverableSnapshot.IsDisconnecting);
            Assert.True(recoverableSnapshot.CanAttemptConnect);
            Assert.False(recoverableSnapshot.IsMonitorConnectedForCurrentGame);
            Assert.Same(detectedGame, recoverableSnapshot.CurrentGame);
            Assert.Same(detectedGame, recoverableSnapshot.ReadResult.DetectedGame);
            Assert.Null(recoverableSnapshot.ReadResult.Stats);
            Assert.Equal(ConnectionState.Detected, recoverableSnapshot.ReadResult.ConnectionState);
            Assert.Equal(GameCompatibilityState.WaitingForMonitor, recoverableSnapshot.EventStatus.CompatibilityState);
            Assert.Equal(DllInjectionState.NotAttempted, recoverableSnapshot.InjectionResult.State);
            Assert.Equal(1, memoryAccessor.AttachCallCount);
            Assert.Equal(1, memoryAccessor.CloseCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(0, eventMonitor.RequestStopCallCount);
        }

        [Fact]
        public void Connect_WhenCurrentGameSupported_InjectsCurrentGameAndRecordsMonitorOwnership()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            int? injectedProcessId = null;
            bool injectionObservedConnectingSnapshot = false;
            List<GameConnectionSnapshot> publishedSnapshots = new();
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            DllInjector dllInjector = CreateDllInjector(
                fileExists: _ => true,
                injectLibrary: (processId, _) =>
                {
                    injectionObservedConnectingSnapshot = publishedSnapshots.Exists(snapshot => snapshot.IsConnecting);
                    injectedProcessId = processId;
                });
            GameConnectionSession session = CreateStartedSession(eventMonitor, detectedGame, dllInjector: dllInjector);
            session.SnapshotChanged += (_, args) => publishedSnapshots.Add(args.Snapshot);

            GameConnectionSnapshot connectedSnapshot = session.Connect();

            Assert.NotEmpty(publishedSnapshots);
            GameConnectionSnapshot connectingSnapshot = publishedSnapshots[0];
            Assert.True(connectingSnapshot.IsConnecting);
            Assert.False(connectingSnapshot.CanAttemptConnect);
            Assert.True(injectionObservedConnectingSnapshot);
            Assert.Equal(1001, injectedProcessId);
            Assert.False(connectedSnapshot.IsConnecting);
            Assert.Same(detectedGame, connectedSnapshot.CurrentGame);
            Assert.Equal(DllInjectionState.Loaded, connectedSnapshot.InjectionResult.State);
            Assert.True(connectedSnapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(connectedSnapshot, publishedSnapshots[^1]);
            Assert.True(session.GetStatusSnapshot().IsMonitorConnectedForCurrentGame);
            Assert.Equal(1, eventMonitor.ReadStatusCallCount);
            Assert.Equal(1001, eventMonitor.LastTargetProcessId);
        }

        [Fact]
        public void Connect_WhenCurrentGameUnsupported_DoesNotAttemptInjection()
        {
            DetectedGame detectedGame = CreateUnsupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            bool injectionAttempted = false;
            DllInjector dllInjector = CreateDllInjector(
                injectLibrary: (_, _) => injectionAttempted = true);
            GameConnectionSession session = CreateStartedSession(eventMonitor, detectedGame, dllInjector: dllInjector);

            GameConnectionSnapshot snapshot = session.Connect();

            Assert.False(snapshot.IsConnecting);
            Assert.Equal(DllInjectionState.NotAttempted, snapshot.InjectionResult.State);
            Assert.False(snapshot.CanAttemptConnect);
            Assert.False(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.False(injectionAttempted);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Connect_WhenNoCurrentGame_DoesNotAttemptInjection()
        {
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            bool injectionAttempted = false;
            DllInjector dllInjector = CreateDllInjector(
                injectLibrary: (_, _) => injectionAttempted = true);
            GameConnectionSession session = CreateStartedSession(eventMonitor, dllInjector: dllInjector);

            GameConnectionSnapshot snapshot = session.Connect();

            Assert.False(snapshot.IsConnecting);
            Assert.Null(snapshot.CurrentGame);
            Assert.Equal(DllInjectionState.NotAttempted, snapshot.InjectionResult.State);
            Assert.False(snapshot.CanAttemptConnect);
            Assert.False(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.False(injectionAttempted);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Connect_WhenCurrentGameChangesDuringInjection_StopsInjectedMonitorWithoutRecordingOwnership()
        {
            DetectedGame originalGame = CreateSupportedGame(processId: 1001);
            DetectedGame changedGame = CreateSupportedGame(processId: 2002);
            int? injectedProcessId = null;
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            SessionContext? context = null;
            DllInjector dllInjector = CreateDllInjector(
                fileExists: _ => true,
                injectLibrary: (processId, _) =>
                {
                    injectedProcessId = processId;
                    context!.EventDetector.Result = changedGame;
                    context.LifecycleEventSource.RaiseStarted(changedGame.ProcessName, changedGame.ProcessId);
                });
            context = CreateSessionContext(eventMonitor, detectedGame: originalGame, dllInjector: dllInjector);
            context.Session.Start();

            GameConnectionSnapshot connectedSnapshot = context.Session.Connect();

            Assert.Equal(1001, injectedProcessId);
            Assert.Same(changedGame, connectedSnapshot.CurrentGame);
            Assert.Equal(DllInjectionState.NotAttempted, connectedSnapshot.InjectionResult.State);
            Assert.False(connectedSnapshot.HasInjectionAttemptForCurrentGame);
            Assert.False(connectedSnapshot.IsMonitorConnectedForCurrentGame);
            Assert.False(context.Session.GetStatusSnapshot().IsMonitorConnectedForCurrentGame);
            Assert.False(context.Session.IsMonitorConnectedFor(originalGame));
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(1, eventMonitor.RequestStopCallCount);
            Assert.Equal(1001, eventMonitor.LastStopTargetProcessId);
        }

        [Fact]
        public void Connect_WhenInjectionThrows_ClearsTransientConnectState()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            DllInjector dllInjector = CreateDllInjector(
                fileExists: _ => throw new NotSupportedException("payload unavailable"));
            GameConnectionSession session = CreateStartedSession(eventMonitor, detectedGame, dllInjector: dllInjector);
            List<GameConnectionSnapshot> publishedSnapshots = new();
            session.SnapshotChanged += (_, args) => publishedSnapshots.Add(args.Snapshot);

            NotSupportedException exception = Assert.Throws<NotSupportedException>(
                () => session.Connect());

            Assert.Equal("payload unavailable", exception.Message);
            Assert.Equal(2, publishedSnapshots.Count);
            Assert.True(publishedSnapshots[0].IsConnecting);
            GameConnectionSnapshot statusSnapshot = session.Snapshot;
            Assert.Equal(statusSnapshot, publishedSnapshots[^1]);
            Assert.False(statusSnapshot.IsConnecting);
            Assert.True(statusSnapshot.CanAttemptConnect);
            Assert.False(statusSnapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Connect_WhenCompletionThrows_StopsMonitorAndClearsConnectionState()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new()
            {
                AttachException = new InvalidOperationException("read failed")
            };
            GameConnectionSession session = CreateStartedSession(
                eventMonitor,
                detectedGame,
                memoryAccessor: memoryAccessor);
            List<GameConnectionSnapshot> publishedSnapshots = new();
            session.SnapshotChanged += (_, args) => publishedSnapshots.Add(args.Snapshot);

            eventMonitor.ResetCalls();
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => session.Connect());

            Assert.Equal("read failed", exception.Message);
            Assert.Equal(2, publishedSnapshots.Count);
            Assert.True(publishedSnapshots[0].IsConnecting);
            GameConnectionSnapshot statusSnapshot = session.Snapshot;
            Assert.Equal(statusSnapshot, publishedSnapshots[^1]);
            Assert.False(statusSnapshot.IsConnecting);
            Assert.True(statusSnapshot.CanAttemptConnect);
            Assert.False(statusSnapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(DllInjectionState.NotAttempted, statusSnapshot.InjectionResult.State);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(1, eventMonitor.RequestStopCallCount);
            Assert.Equal(1001, eventMonitor.LastStopTargetProcessId);
        }

        [Fact]
        public void Read_WhenMonitorIsConnectedForCurrentGame_ReadsEventMonitor()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            GameEventMonitorStatus compatibleStatus = CreateCompatibleStatus();
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = compatibleStatus
            };
            var timeProvider = new FakeTimeProvider();
            GameConnectionSession session = CreateStartedSession(eventMonitor, detectedGame, timeProvider);
            CompleteConnectWithLoadedMonitor(session);

            GameConnectionSnapshot snapshot = session.Read();

            Assert.Same(compatibleStatus, snapshot.EventStatus);
            Assert.True(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(2, eventMonitor.ReadStatusCallCount);
            Assert.Equal(timeProvider.GetUtcNow(), eventMonitor.LastReceivedAt);
            Assert.Equal(1001, eventMonitor.LastTargetProcessId);
        }

        [Fact]
        public void Read_WhenCurrentGameChangesAfterConnect_StopsOldMonitorAndDoesNotReadEventMonitor()
        {
            DetectedGame connectedGame = CreateSupportedGame(processId: 1001);
            DetectedGame detectedGame = CreateSupportedGame(processId: 2002);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            SessionContext context = CreateSessionContext(
                eventMonitor,
                detectedGame: connectedGame,
                memoryAccessor: memoryAccessor);
            context.Session.Start();
            CompleteConnectWithLoadedMonitor(context.Session);
            int attachCallCount = memoryAccessor.AttachCallCount;
            eventMonitor.ResetCalls();
            context.EventDetector.Result = detectedGame;
            context.LifecycleEventSource.RaiseStarted(detectedGame.ProcessName, detectedGame.ProcessId);

            GameConnectionSnapshot snapshot = context.Session.Read();

            Assert.Same(detectedGame, snapshot.CurrentGame);
            Assert.Same(detectedGame, snapshot.ReadResult.DetectedGame);
            Assert.NotNull(snapshot.ReadResult.Stats);
            Assert.Equal(ConnectionState.Connected, snapshot.ReadResult.ConnectionState);
            Assert.Equal(DllInjectionState.NotAttempted, snapshot.InjectionResult.State);
            Assert.Equal(GameCompatibilityState.WaitingForMonitor, snapshot.EventStatus.CompatibilityState);
            Assert.False(snapshot.HasInjectionAttemptForCurrentGame);
            Assert.False(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(attachCallCount + 1, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(1, eventMonitor.RequestStopCallCount);
            Assert.Equal(1001, eventMonitor.LastStopTargetProcessId);
        }

        [Fact]
        public void Read_WhenCurrentGameChangesDuringStatsRead_ReturnsCurrentStatusSnapshot()
        {
            DetectedGame connectedGame = CreateSupportedGame(processId: 1001);
            DetectedGame detectedGame = CreateSupportedGame(processId: 2002);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            SessionContext context = CreateSessionContext(
                eventMonitor,
                detectedGame: connectedGame,
                memoryAccessor: memoryAccessor);
            context.Session.Start();
            CompleteConnectWithLoadedMonitor(context.Session);
            eventMonitor.ResetCalls();
            memoryAccessor.AttachCallback = (_, _) =>
            {
                context.EventDetector.Result = detectedGame;
                context.LifecycleEventSource.RaiseStarted(detectedGame.ProcessName, detectedGame.ProcessId);
            };

            GameConnectionSnapshot snapshot = context.Session.Read();

            Assert.Same(detectedGame, snapshot.CurrentGame);
            Assert.Same(detectedGame, snapshot.ReadResult.DetectedGame);
            Assert.Null(snapshot.ReadResult.Stats);
            Assert.Equal(ConnectionState.Detected, snapshot.ReadResult.ConnectionState);
            Assert.Equal(DllInjectionState.NotAttempted, snapshot.InjectionResult.State);
            Assert.False(snapshot.HasInjectionAttemptForCurrentGame);
            Assert.False(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.False(context.Session.GetStatusSnapshot().IsMonitorConnectedForCurrentGame);
            Assert.False(context.Session.IsMonitorConnectedFor(connectedGame));
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(1, eventMonitor.RequestStopCallCount);
            Assert.Equal(1001, eventMonitor.LastStopTargetProcessId);
        }

        [Fact]
        public void Read_WhenMonitorReadinessTimesOut_RequestsStopAndMarksInjectionFailed()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = GameEventMonitorStatus.WaitingForMonitor
            };
            var timeProvider = new FakeTimeProvider();
            GameConnectionSession session = CreateStartedSession(eventMonitor, detectedGame, timeProvider);
            CompleteConnectWithLoadedMonitor(session);
            eventMonitor.ResetCalls();

            timeProvider.Advance(TimeSpan.FromSeconds(16));
            GameConnectionSnapshot snapshot = session.Read();

            Assert.Equal(DllInjectionState.Failed, snapshot.InjectionResult.State);
            Assert.False(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(1, eventMonitor.RequestStopCallCount);
            Assert.Equal(1001, eventMonitor.LastStopTargetProcessId);
        }

        [Fact]
        public void BeginDisconnect_WhenMonitorIsConnected_RequestsStopAndReturnsDisconnectingSnapshot()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            GameConnectionSession session = CreateStartedSession(eventMonitor, detectedGame);
            CompleteConnectWithLoadedMonitor(session);
            eventMonitor.ResetCalls();

            GameConnectionRefreshResult snapshot = session.BeginDisconnect();

            Assert.True(snapshot.IsDisconnecting);
            Assert.True(session.GetStatusSnapshot().IsDisconnecting);
            Assert.False(snapshot.CanAttemptConnect);
            Assert.True(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(GameCompatibilityState.WaitingForMonitor, snapshot.EventStatus.CompatibilityState);
            Assert.Equal(1, eventMonitor.RequestStopCallCount);
            Assert.Equal(1001, eventMonitor.LastStopTargetProcessId);
            Assert.Equal(0, eventMonitor.IsStopCompleteCallCount);
        }

        [Fact]
        public void HandleReadFailure_WhenMonitorIsConnected_PreservesMonitorOwnershipAndReturnsConnectedSnapshot()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            GameEventMonitorStatus compatibleStatus = CreateCompatibleStatus();
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = compatibleStatus
            };
            GameConnectionSession session = CreateStartedSession(eventMonitor, detectedGame);
            CompleteConnectWithLoadedMonitor(session);
            session.BeginDisconnect();
            eventMonitor.ResetCalls();

            GameConnectionSnapshot cleanupSnapshot = session.HandleReadFailure();
            GameConnectionSnapshot readSnapshot = session.Read();

            Assert.False(cleanupSnapshot.IsConnecting);
            Assert.False(cleanupSnapshot.IsDisconnecting);
            Assert.False(cleanupSnapshot.CanAttemptConnect);
            Assert.True(cleanupSnapshot.IsMonitorConnectedForCurrentGame);
            Assert.Same(detectedGame, cleanupSnapshot.CurrentGame);
            Assert.Same(detectedGame, cleanupSnapshot.ReadResult.DetectedGame);
            Assert.Null(cleanupSnapshot.ReadResult.Stats);
            Assert.Equal(ConnectionState.Connected, cleanupSnapshot.ReadResult.ConnectionState);
            Assert.Equal(DllInjectionState.Loaded, cleanupSnapshot.InjectionResult.State);
            Assert.Equal(GameCompatibilityState.WaitingForMonitor, cleanupSnapshot.EventStatus.CompatibilityState);
            Assert.Same(compatibleStatus, readSnapshot.EventStatus);
            Assert.True(readSnapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(1, eventMonitor.ReadStatusCallCount);
            Assert.Equal(0, eventMonitor.RequestStopCallCount);
        }

        [Fact]
        public void Read_WhenDisconnectStopDoesNotCompleteBeforeTimeout_ReturnsDisconnectingSnapshot()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                IsStopCompleteResult = false,
                Status = CreateCompatibleStatus()
            };
            var timeProvider = new FakeTimeProvider();
            GameConnectionSession session = CreateStartedSession(eventMonitor, detectedGame, timeProvider);
            CompleteConnectWithLoadedMonitor(session);
            session.BeginDisconnect();
            eventMonitor.ResetCalls();

            GameConnectionSnapshot snapshot = session.Read();

            Assert.True(snapshot.IsDisconnecting);
            Assert.True(session.GetStatusSnapshot().IsDisconnecting);
            Assert.Equal(GameCompatibilityState.WaitingForMonitor, snapshot.EventStatus.CompatibilityState);
            Assert.True(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(0, eventMonitor.RequestStopCallCount);
            Assert.Equal(1, eventMonitor.IsStopCompleteCallCount);
            Assert.Equal(1001, eventMonitor.LastStopCompleteTargetProcessId);
        }

        [Fact]
        public void Read_WhenDisconnectStopCompletes_ResetsMonitorOwnershipAndReturnsWaitingSnapshot()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                IsStopCompleteResult = true,
                Status = CreateCompatibleStatus()
            };
            var timeProvider = new FakeTimeProvider();
            GameConnectionSession session = CreateStartedSession(eventMonitor, detectedGame, timeProvider);
            CompleteConnectWithLoadedMonitor(session);
            session.BeginDisconnect();
            eventMonitor.ResetCalls();

            GameConnectionSnapshot snapshot = session.Read();

            Assert.False(snapshot.IsDisconnecting);
            Assert.False(session.GetStatusSnapshot().IsDisconnecting);
            Assert.Equal(DllInjectionState.NotAttempted, snapshot.InjectionResult.State);
            Assert.False(snapshot.HasInjectionAttemptForCurrentGame);
            Assert.False(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(GameCompatibilityState.WaitingForMonitor, snapshot.EventStatus.CompatibilityState);
            Assert.Equal(0, eventMonitor.RequestStopCallCount);
            Assert.Equal(1, eventMonitor.IsStopCompleteCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Read_WhenDisconnectStopTimesOut_ResetsMonitorOwnershipAndReturnsWaitingSnapshot()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                IsStopCompleteResult = false,
                Status = CreateCompatibleStatus()
            };
            var timeProvider = new FakeTimeProvider();
            GameConnectionSession session = CreateStartedSession(eventMonitor, detectedGame, timeProvider);
            CompleteConnectWithLoadedMonitor(session);
            session.BeginDisconnect();
            eventMonitor.ResetCalls();

            timeProvider.Advance(TimeSpan.FromSeconds(3));
            GameConnectionSnapshot snapshot = session.Read();

            Assert.False(snapshot.IsDisconnecting);
            Assert.False(session.GetStatusSnapshot().IsDisconnecting);
            Assert.Equal(DllInjectionState.NotAttempted, snapshot.InjectionResult.State);
            Assert.False(snapshot.HasInjectionAttemptForCurrentGame);
            Assert.False(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(GameCompatibilityState.WaitingForMonitor, snapshot.EventStatus.CompatibilityState);
            Assert.Equal(0, eventMonitor.RequestStopCallCount);
            Assert.Equal(1, eventMonitor.IsStopCompleteCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        private static GameConnectionSession CreateStartedSession(
            FakeGameEventMonitor eventMonitor,
            DetectedGame? detectedGame = null,
            TimeProvider? timeProvider = null,
            FakeProcessMemoryAccessor? memoryAccessor = null,
            DllInjector? dllInjector = null)
        {
            SessionContext context = CreateSessionContext(
                eventMonitor,
                timeProvider,
                detectedGame,
                memoryAccessor: memoryAccessor,
                dllInjector: dllInjector);
            context.Session.Start();
            return context.Session;
        }

        private static SessionContext CreateSessionContext(
            FakeGameEventMonitor eventMonitor,
            TimeProvider? timeProvider = null,
            DetectedGame? detectedGame = null,
            DetectedGame? pollingDetectedGame = null,
            FakeProcessMemoryAccessor? memoryAccessor = null,
            FakeProcessLifecycleEventSource? lifecycleEventSource = null,
            DllInjector? dllInjector = null)
        {
            FakeGameProcessDetector eventDetector = new()
            {
                Result = detectedGame
            };
            FakeGameProcessDetector pollingProcessDetector = new()
            {
                Result = pollingDetectedGame
            };
            memoryAccessor ??= new FakeProcessMemoryAccessor();
            lifecycleEventSource ??= new FakeProcessLifecycleEventSource();

            GameConnectionSession session = new(
                new GameMemoryReader(memoryAccessor),
                new GameProcessDetectionService(eventDetector, lifecycleEventSource),
                pollingProcessDetector,
                dllInjector ?? CreateDllInjector(),
                eventMonitor,
                timeProvider ?? TimeProvider.System);
            return new SessionContext(
                session,
                eventDetector,
                pollingProcessDetector,
                lifecycleEventSource);
        }

        private static DetectedGame CreateSupportedGame(int processId)
        {
            return new DetectedGame(
                GameVariant.SteamZombies,
                "Steam Zombies",
                "t6zm",
                processId,
                PlayerStatAddressMap.SteamZombies,
                null);
        }

        private static DetectedGame CreateUnsupportedGame(int processId)
        {
            return new DetectedGame(
                GameVariant.RedactedZombies,
                "Redacted Zombies",
                "t6zm",
                processId,
                null,
                "Unsupported variant");
        }

        private static DllInjector CreateDllInjector(
            Func<string, bool>? fileExists = null,
            Action<int, string>? injectLibrary = null)
        {
            return new DllInjector(
                () => false,
                () => string.Empty,
                fileExists ?? (_ => true),
                _ => DllInjector.DllPayloadValidationResult.Valid,
                _ => false,
                injectLibrary ?? ((_, _) => { }),
                (_, _) => { });
        }

        private static GameConnectionSnapshot CompleteConnectWithLoadedMonitor(GameConnectionSession session)
        {
            GameConnectionSnapshot connectedSnapshot = session.Connect();
            Assert.Equal(DllInjectionState.Loaded, connectedSnapshot.InjectionResult.State);
            Assert.False(connectedSnapshot.IsConnecting);
            Assert.True(connectedSnapshot.IsMonitorConnectedForCurrentGame);
            return connectedSnapshot;
        }

        private static GameEventMonitorStatus CreateCompatibleStatus()
        {
            return new GameEventMonitorStatus(
                GameCompatibilityState.Compatible,
                0,
                0,
                1,
                Array.Empty<GameEvent>());
        }

        private sealed class SessionContext(
            GameConnectionSession session,
            FakeGameProcessDetector eventDetector,
            FakeGameProcessDetector pollingProcessDetector,
            FakeProcessLifecycleEventSource lifecycleEventSource)
        {
            public GameConnectionSession Session { get; } = session;

            public FakeGameProcessDetector EventDetector { get; } = eventDetector;

            public FakeGameProcessDetector PollingProcessDetector { get; } = pollingProcessDetector;

            public FakeProcessLifecycleEventSource LifecycleEventSource { get; } = lifecycleEventSource;
        }

        private sealed class FakeGameEventMonitor : IGameEventMonitor
        {
            public GameEventMonitorStatus Status { get; set; } = GameEventMonitorStatus.WaitingForMonitor;

            public bool IsStopCompleteResult { get; set; } = true;

            public int ReadStatusCallCount { get; private set; }

            public int RequestStopCallCount { get; private set; }

            public int IsStopCompleteCallCount { get; private set; }

            public DateTimeOffset? LastReceivedAt { get; private set; }

            public int? LastTargetProcessId { get; private set; }

            public int? LastStopTargetProcessId { get; private set; }

            public int? LastStopCompleteTargetProcessId { get; private set; }

            public GameEventMonitorStatus ReadStatus(DateTimeOffset receivedAt, int? targetProcessId)
            {
                ReadStatusCallCount++;
                LastReceivedAt = receivedAt;
                LastTargetProcessId = targetProcessId;
                return Status;
            }

            public void RequestStop(int? targetProcessId)
            {
                RequestStopCallCount++;
                LastStopTargetProcessId = targetProcessId;
            }

            public bool IsStopComplete(int targetProcessId)
            {
                IsStopCompleteCallCount++;
                LastStopCompleteTargetProcessId = targetProcessId;
                return IsStopCompleteResult;
            }

            public void ResetCalls()
            {
                ReadStatusCallCount = 0;
                RequestStopCallCount = 0;
                IsStopCompleteCallCount = 0;
                LastReceivedAt = null;
                LastTargetProcessId = null;
                LastStopTargetProcessId = null;
                LastStopCompleteTargetProcessId = null;
            }

            public void Dispose()
            {
            }
        }

        private sealed class FakeProcessLifecycleEventSource : IProcessLifecycleEventSource
        {
            public event EventHandler<ProcessLifecycleEventArgs>? ProcessStarted;

            public event EventHandler<ProcessLifecycleEventArgs>? ProcessStopped;

            public Exception? StartException { get; set; }

            public void Start()
            {
                if (StartException is not null)
                {
                    throw StartException;
                }
            }

            public void RaiseStarted(string processName, int processId)
            {
                ProcessStarted?.Invoke(this, new ProcessLifecycleEventArgs(processName, processId));
            }

            public void RaiseStopped(string processName, int processId)
            {
                ProcessStopped?.Invoke(this, new ProcessLifecycleEventArgs(processName, processId));
            }

            public void Dispose()
            {
            }
        }
    }
}
