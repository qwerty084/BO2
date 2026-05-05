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
            Assert.Equal(GameConnectionPhase.Detected, snapshot.ConnectionPhase);
            Assert.Null(snapshot.ReadResult);
            Assert.Equal(GameConnectionEventMonitorState.Waiting, snapshot.EventMonitorSummary.State);
            Assert.Same(GameEventMonitorStatus.WaitingForMonitor, snapshot.EventMonitorSummary.Status);
            AssertCommandAvailability(
                snapshot,
                connectEnabled: true,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Snapshot_WhenSessionStartsWithoutCurrentGame_ExposesNoGameStatusState()
        {
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            GameConnectionSession session = CreateStartedSession(
                eventMonitor,
                memoryAccessor: memoryAccessor);

            GameConnectionSnapshot snapshot = session.Snapshot;

            Assert.Null(snapshot.CurrentGame);
            Assert.Equal(GameConnectionPhase.NoGame, snapshot.ConnectionPhase);
            Assert.Null(snapshot.ReadResult);
            Assert.Equal(GameConnectionEventMonitorState.Waiting, snapshot.EventMonitorSummary.State);
            AssertCommandAvailability(
                snapshot,
                connectEnabled: false,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Snapshot_WhenSessionStartsWithUnsupportedGame_ExposesUnsupportedStatusState()
        {
            DetectedGame detectedGame = CreateUnsupportedGame(processId: 1001);
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
            Assert.Equal(GameConnectionPhase.UnsupportedGame, snapshot.ConnectionPhase);
            Assert.Null(snapshot.ReadResult);
            Assert.Equal(GameConnectionEventMonitorState.Waiting, snapshot.EventMonitorSummary.State);
            AssertCommandAvailability(
                snapshot,
                connectEnabled: false,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Read_WhenPreConnectCurrentGameSupported_ReturnsDetectedStatusOnlyWithoutPublishingSnapshotChanged()
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
            List<GameConnectionSnapshotChangedEventArgs> changes = [];
            session.SnapshotChanged += (_, args) => changes.Add(args);

            GameConnectionSnapshot readSnapshot = session.Read();

            Assert.Empty(changes);
            Assert.Equal(initialSnapshot, readSnapshot);
            Assert.Same(detectedGame, readSnapshot.CurrentGame);
            Assert.Equal(GameConnectionPhase.Detected, readSnapshot.ConnectionPhase);
            Assert.Null(readSnapshot.ReadResult);
            AssertCommandAvailability(
                readSnapshot,
                connectEnabled: true,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
            Assert.Equal(readSnapshot, session.Snapshot);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Read_WhenStatusOnlyCurrentGameChangesBeforeReturn_ReturnsUpdatedCurrentGame()
        {
            DetectedGame originalGame = CreateSupportedGame(processId: 1001);
            DetectedGame changedGame = CreateSupportedGame(processId: 2002);
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
            memoryAccessor.CloseCallback = () =>
            {
                context.EventDetector.Result = changedGame;
                context.LifecycleEventSource.RaiseStarted(changedGame.ProcessName, changedGame.ProcessId);
            };

            GameConnectionSnapshot snapshot = context.Session.Read();

            Assert.Same(changedGame, snapshot.CurrentGame);
            Assert.Equal(GameConnectionPhase.Detected, snapshot.ConnectionPhase);
            Assert.Null(snapshot.ReadResult);
            Assert.Equal(snapshot, context.Session.Snapshot);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
            Assert.Equal(1, memoryAccessor.CloseCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Read_WhenStatusOnlyLifecycleChangesBeforeReturn_RecomputesReadPlan()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            GameEventMonitorStatus compatibleStatus = CreateCompatibleStatus();
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = compatibleStatus
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            SessionContext context = CreateSessionContext(
                eventMonitor,
                detectedGame: detectedGame,
                memoryAccessor: memoryAccessor);
            context.Session.Start();
            bool connectedDuringClear = false;
            memoryAccessor.CloseCallback = () =>
            {
                if (connectedDuringClear)
                {
                    return;
                }

                connectedDuringClear = true;
                CompleteConnectWithLoadedMonitor(context.Session);
            };

            GameConnectionSnapshot snapshot = context.Session.Read();

            Assert.True(connectedDuringClear);
            Assert.Same(detectedGame, snapshot.CurrentGame);
            Assert.Equal(GameConnectionPhase.Connected, snapshot.ConnectionPhase);
            Assert.NotNull(snapshot.ReadResult);
            Assert.Equal(GameConnectionEventMonitorState.Ready, snapshot.EventMonitorSummary.State);
            Assert.Same(compatibleStatus, snapshot.EventMonitorSummary.Status);
            AssertCommandAvailability(
                snapshot,
                connectEnabled: false,
                connectVisible: false,
                disconnectEnabled: true,
                disconnectVisible: true);
            Assert.Equal(snapshot, context.Session.Snapshot);
            Assert.Equal(1, memoryAccessor.CloseCallCount);
        }

        [Fact]
        public void Read_WhenConnectedObservableStateIsUnchanged_DoesNotPublishSnapshotChanged()
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
            GameConnectionSnapshot firstSnapshot = CompleteConnectWithLoadedMonitor(session);
            List<GameConnectionSnapshotChangedEventArgs> changes = [];
            session.SnapshotChanged += (_, args) => changes.Add(args);

            GameConnectionSnapshot secondSnapshot = session.Read();

            Assert.Empty(changes);
            Assert.Equal(firstSnapshot, secondSnapshot);
            Assert.Equal(firstSnapshot, session.Snapshot);
            Assert.Equal(2, memoryAccessor.AttachCallCount);
            Assert.Equal(2, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Read_WhenNoCurrentGame_ReturnsNoGameSnapshot()
        {
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            GameConnectionSession session = CreateStartedSession(
                eventMonitor,
                memoryAccessor: memoryAccessor);

            GameConnectionSnapshot snapshot = session.Read();

            Assert.Null(snapshot.CurrentGame);
            Assert.Equal(GameConnectionPhase.NoGame, snapshot.ConnectionPhase);
            Assert.Null(snapshot.ReadResult);
            AssertCommandAvailability(
                snapshot,
                connectEnabled: false,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
            Assert.Equal(1, memoryAccessor.CloseCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(snapshot, session.Snapshot);
        }

        [Fact]
        public void Read_WhenCurrentGameSupportedBeforeConnect_ReturnsDetectedSnapshotWithoutActiveReads()
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
            Assert.Equal(GameConnectionPhase.Detected, snapshot.ConnectionPhase);
            Assert.Null(snapshot.ReadResult);
            AssertCommandAvailability(
                snapshot,
                connectEnabled: true,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(snapshot, session.Snapshot);
        }

        [Fact]
        public void Read_WhenConnectedGameChangesToSupportedBeforeReconnect_ClearsAttachedGame()
        {
            DetectedGame originalGame = CreateSupportedGame(processId: 1001);
            DetectedGame changedGame = CreateSupportedGame(processId: 2002);
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
            CompleteConnectWithLoadedMonitor(context.Session);
            int attachCallCount = memoryAccessor.AttachCallCount;
            int closeCallCount = memoryAccessor.CloseCallCount;
            context.EventDetector.Result = changedGame;
            context.LifecycleEventSource.RaiseStarted(changedGame.ProcessName, changedGame.ProcessId);
            eventMonitor.ResetCalls();

            GameConnectionSnapshot snapshot = context.Session.Read();

            Assert.Same(changedGame, snapshot.CurrentGame);
            Assert.Equal(GameConnectionPhase.Detected, snapshot.ConnectionPhase);
            Assert.Null(snapshot.ReadResult);
            Assert.Equal(attachCallCount, memoryAccessor.AttachCallCount);
            Assert.Equal(closeCallCount + 1, memoryAccessor.CloseCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(snapshot, context.Session.Snapshot);
        }

        [Fact]
        public void Read_WhenCurrentGameUnsupported_ReturnsUnsupportedSnapshot()
        {
            DetectedGame detectedGame = CreateUnsupportedGame(processId: 1001);
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
            Assert.Equal(GameConnectionPhase.UnsupportedGame, snapshot.ConnectionPhase);
            Assert.Null(snapshot.ReadResult);
            AssertCommandAvailability(
                snapshot,
                connectEnabled: false,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
            Assert.Equal(1, memoryAccessor.CloseCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(snapshot, session.Snapshot);
        }

        [Fact]
        public void Read_WhenPollingFallbackIsActive_UpdatesCurrentGameBeforeReturningStatusOnly()
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
            Assert.Equal(GameConnectionPhase.Detected, snapshot.ConnectionPhase);
            Assert.Null(snapshot.ReadResult);
            Assert.Equal(1, context.PollingProcessDetector.DetectCallCount);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            AssertCommandAvailability(
                snapshot,
                connectEnabled: true,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
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
            Assert.Equal(GameConnectionPhase.Detected, snapshot.ConnectionPhase);
            Assert.Null(snapshot.ReadResult);
            AssertCommandAvailability(
                snapshot,
                connectEnabled: true,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(0, eventMonitor.RequestStopCallCount);
            Assert.Equal(snapshot, context.Session.Snapshot);
        }

        [Fact]
        public void GetStatusSnapshot_WhenNoCurrentGame_ReturnsNoGameStatusWithoutPlayerStatsRead()
        {
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            GameConnectionSession session = CreateStartedSession(
                eventMonitor,
                memoryAccessor: memoryAccessor);

            GameConnectionSnapshot snapshot = session.GetStatusSnapshot();

            Assert.Null(snapshot.CurrentGame);
            Assert.Equal(GameConnectionPhase.NoGame, snapshot.ConnectionPhase);
            Assert.Null(snapshot.ReadResult);
            AssertCommandAvailability(
                snapshot,
                connectEnabled: false,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(snapshot, session.Snapshot);
        }

        [Fact]
        public void GetStatusSnapshot_WhenCurrentGameUnsupported_ReturnsUnsupportedStatusWithoutPlayerStatsRead()
        {
            DetectedGame detectedGame = CreateUnsupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            GameConnectionSession session = CreateStartedSession(
                eventMonitor,
                detectedGame,
                memoryAccessor: memoryAccessor);

            GameConnectionSnapshot snapshot = session.GetStatusSnapshot();

            Assert.Same(detectedGame, snapshot.CurrentGame);
            Assert.Equal(GameConnectionPhase.UnsupportedGame, snapshot.ConnectionPhase);
            Assert.Null(snapshot.ReadResult);
            AssertCommandAvailability(
                snapshot,
                connectEnabled: false,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(snapshot, session.Snapshot);
        }

        [Fact]
        public void ProcessDetectionChanged_WhenCurrentGameChanges_PublishesSnapshotTransition()
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
            List<GameConnectionSnapshotChangedEventArgs> changes = [];
            context.Session.SnapshotChanged += (_, args) => changes.Add(args);
            context.EventDetector.Result = detectedGame;

            context.LifecycleEventSource.RaiseStarted(detectedGame.ProcessName, detectedGame.ProcessId);

            GameConnectionSnapshotChangedEventArgs change = Assert.Single(changes);
            Assert.Same(originalGame, change.PreviousSnapshot.CurrentGame);
            Assert.Same(detectedGame, change.Snapshot.CurrentGame);
            Assert.Equal(GameConnectionPhase.Detected, change.Snapshot.ConnectionPhase);
            Assert.Null(change.Snapshot.ReadResult);
            Assert.Equal(GameConnectionEventMonitorState.Waiting, change.Snapshot.EventMonitorSummary.State);
            Assert.Equal(change.Snapshot, context.Session.Snapshot);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void ProcessDetectionChanged_WhenCurrentGameStops_PublishesNoGameTransition()
        {
            DetectedGame originalGame = CreateSupportedGame(processId: 1001);
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
            List<GameConnectionSnapshotChangedEventArgs> changes = [];
            context.Session.SnapshotChanged += (_, args) => changes.Add(args);
            context.EventDetector.Result = null;

            context.LifecycleEventSource.RaiseStopped(originalGame.ProcessName, originalGame.ProcessId);

            GameConnectionSnapshotChangedEventArgs change = Assert.Single(changes);
            Assert.Same(originalGame, change.PreviousSnapshot.CurrentGame);
            Assert.Null(change.Snapshot.CurrentGame);
            Assert.Equal(GameConnectionPhase.NoGame, change.Snapshot.ConnectionPhase);
            Assert.Null(change.Snapshot.ReadResult);
            Assert.Equal(GameConnectionEventMonitorState.Waiting, change.Snapshot.EventMonitorSummary.State);
            Assert.Equal(change.Snapshot, context.Session.Snapshot);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void ProcessDetectionChanged_WhenCurrentGameChangesToUnsupported_PublishesUnsupportedTransition()
        {
            DetectedGame originalGame = CreateSupportedGame(processId: 1001);
            DetectedGame detectedGame = CreateUnsupportedGame(processId: 2002);
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
            List<GameConnectionSnapshotChangedEventArgs> changes = [];
            context.Session.SnapshotChanged += (_, args) => changes.Add(args);
            context.EventDetector.Result = detectedGame;

            context.LifecycleEventSource.RaiseStarted(detectedGame.ProcessName, detectedGame.ProcessId);

            GameConnectionSnapshotChangedEventArgs change = Assert.Single(changes);
            Assert.Same(originalGame, change.PreviousSnapshot.CurrentGame);
            Assert.Same(detectedGame, change.Snapshot.CurrentGame);
            Assert.Equal(GameConnectionPhase.UnsupportedGame, change.Snapshot.ConnectionPhase);
            Assert.Null(change.Snapshot.ReadResult);
            Assert.Equal(GameConnectionEventMonitorState.Waiting, change.Snapshot.EventMonitorSummary.State);
            Assert.Equal(change.Snapshot, context.Session.Snapshot);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Connect_WhenInjectionIsInProgress_GetStatusSnapshotReturnsConnectingStatusWithoutPlayerStatsRead()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            GameConnectionSession? session = null;
            GameConnectionSnapshot? statusSnapshotDuringInjection = null;
            int attachCallCountDuringInjection = -1;
            int readStatusCallCountDuringInjection = -1;
            DllInjector dllInjector = CreateDllInjector(
                fileExists: _ => true,
                injectLibrary: (_, _) =>
                {
                    statusSnapshotDuringInjection = session!.GetStatusSnapshot();
                    attachCallCountDuringInjection = memoryAccessor.AttachCallCount;
                    readStatusCallCountDuringInjection = eventMonitor.ReadStatusCallCount;
                });
            session = CreateStartedSession(
                eventMonitor,
                detectedGame,
                memoryAccessor: memoryAccessor,
                dllInjector: dllInjector);

            GameConnectionSnapshot connectedSnapshot = session.Connect();

            Assert.True(statusSnapshotDuringInjection.HasValue);
            GameConnectionSnapshot statusSnapshot = statusSnapshotDuringInjection.Value;
            Assert.Equal(GameConnectionPhase.Connecting, statusSnapshot.ConnectionPhase);
            Assert.Equal(GameConnectionEventMonitorState.Connecting, statusSnapshot.EventMonitorSummary.State);
            AssertCommandAvailability(
                statusSnapshot,
                connectEnabled: false,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
            Assert.Same(detectedGame, statusSnapshot.CurrentGame);
            Assert.Null(statusSnapshot.ReadResult);
            Assert.Equal(0, attachCallCountDuringInjection);
            Assert.Equal(0, readStatusCallCountDuringInjection);
            Assert.Equal(GameConnectionPhase.Connected, connectedSnapshot.ConnectionPhase);
            Assert.Equal(GameConnectionEventMonitorState.Ready, connectedSnapshot.EventMonitorSummary.State);
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
            session.Disconnect();
            int attachCallCount = memoryAccessor.AttachCallCount;
            eventMonitor.ResetCalls();

            GameConnectionSnapshot statusSnapshot = session.GetStatusSnapshot();

            Assert.Equal(GameConnectionPhase.Disconnecting, statusSnapshot.ConnectionPhase);
            Assert.Equal(GameConnectionEventMonitorState.Disconnecting, statusSnapshot.EventMonitorSummary.State);
            Assert.Same(detectedGame, statusSnapshot.CurrentGame);
            Assert.Null(statusSnapshot.ReadResult);
            Assert.Equal(attachCallCount, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(0, eventMonitor.IsStopCompleteCallCount);
        }

        [Fact]
        public void Connect_WhenCurrentGameSupported_InjectsCurrentGameAndPublishesConnectedState()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            int? injectedProcessId = null;
            bool injectionObservedConnectingSnapshot = false;
            List<GameConnectionSnapshot> publishedSnapshots = [];
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            DllInjector dllInjector = CreateDllInjector(
                fileExists: _ => true,
                injectLibrary: (processId, _) =>
                {
                    injectionObservedConnectingSnapshot = publishedSnapshots.Exists(
                        snapshot => snapshot.ConnectionPhase == GameConnectionPhase.Connecting);
                    injectedProcessId = processId;
                });
            GameConnectionSession session = CreateStartedSession(
                eventMonitor,
                detectedGame,
                memoryAccessor: memoryAccessor,
                dllInjector: dllInjector);
            session.SnapshotChanged += (_, args) => publishedSnapshots.Add(args.Snapshot);

            GameConnectionSnapshot connectedSnapshot = session.Connect();

            Assert.NotEmpty(publishedSnapshots);
            GameConnectionSnapshot connectingSnapshot = publishedSnapshots[0];
            Assert.Equal(GameConnectionPhase.Connecting, connectingSnapshot.ConnectionPhase);
            AssertCommandAvailability(
                connectingSnapshot,
                connectEnabled: false,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
            Assert.True(injectionObservedConnectingSnapshot);
            Assert.Equal(1001, injectedProcessId);
            Assert.Same(detectedGame, connectedSnapshot.CurrentGame);
            Assert.Equal(GameConnectionPhase.Connected, connectedSnapshot.ConnectionPhase);
            Assert.NotNull(connectedSnapshot.ReadResult);
            Assert.Same(detectedGame, connectedSnapshot.ReadResult.DetectedGame);
            Assert.Equal(GameConnectionEventMonitorState.Ready, connectedSnapshot.EventMonitorSummary.State);
            AssertCommandAvailability(
                connectedSnapshot,
                connectEnabled: false,
                connectVisible: false,
                disconnectEnabled: true,
                disconnectVisible: true);
            Assert.Equal(connectedSnapshot, publishedSnapshots[^1]);
            Assert.Equal(GameConnectionPhase.Connected, session.GetStatusSnapshot().ConnectionPhase);
            Assert.Equal(1, memoryAccessor.AttachCallCount);
            Assert.Equal(1, eventMonitor.ReadStatusCallCount);
            Assert.Equal(1001, eventMonitor.LastTargetProcessId);
        }

        [Fact]
        public void Connect_WhenCompletingConnect_RecordsMonitorOwnershipBeforeFirstActiveRead()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            GameConnectionSession? session = null;
            GameConnectionSnapshot? statusSnapshotDuringStatsRead = null;
            int readStatusCallCountDuringStatsRead = -1;
            memoryAccessor.AttachCallback = (_, _) =>
            {
                statusSnapshotDuringStatsRead = session!.GetStatusSnapshot();
                readStatusCallCountDuringStatsRead = eventMonitor.ReadStatusCallCount;
            };
            session = CreateStartedSession(
                eventMonitor,
                detectedGame,
                memoryAccessor: memoryAccessor);

            GameConnectionSnapshot connectedSnapshot = session.Connect();

            Assert.True(statusSnapshotDuringStatsRead.HasValue);
            GameConnectionSnapshot statusSnapshot = statusSnapshotDuringStatsRead.Value;
            Assert.Equal(GameConnectionPhase.Connected, statusSnapshot.ConnectionPhase);
            Assert.Same(detectedGame, statusSnapshot.CurrentGame);
            Assert.Null(statusSnapshot.ReadResult);
            AssertCommandAvailability(
                statusSnapshot,
                connectEnabled: false,
                connectVisible: false,
                disconnectEnabled: true,
                disconnectVisible: true);
            Assert.Equal(0, readStatusCallCountDuringStatsRead);
            Assert.Equal(GameConnectionPhase.Connected, connectedSnapshot.ConnectionPhase);
            Assert.NotNull(connectedSnapshot.ReadResult);
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

            Assert.Equal(GameConnectionPhase.UnsupportedGame, snapshot.ConnectionPhase);
            Assert.Equal(GameConnectionEventMonitorState.Waiting, snapshot.EventMonitorSummary.State);
            AssertCommandAvailability(
                snapshot,
                connectEnabled: false,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
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

            Assert.Null(snapshot.CurrentGame);
            Assert.Equal(GameConnectionPhase.NoGame, snapshot.ConnectionPhase);
            Assert.Equal(GameConnectionEventMonitorState.Waiting, snapshot.EventMonitorSummary.State);
            AssertCommandAvailability(
                snapshot,
                connectEnabled: false,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
            Assert.False(injectionAttempted);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Connect_WhenMonitorLoadingFails_ReturnsDetectedStatusOnlyWithLoadingFailedSummary()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            DllInjector dllInjector = CreateDllInjector(fileExists: _ => false);
            GameConnectionSession session = CreateStartedSession(
                eventMonitor,
                detectedGame,
                memoryAccessor: memoryAccessor,
                dllInjector: dllInjector);

            GameConnectionSnapshot snapshot = session.Connect();

            Assert.Equal(GameConnectionPhase.Detected, snapshot.ConnectionPhase);
            Assert.Equal(GameConnectionEventMonitorState.LoadingFailed, snapshot.EventMonitorSummary.State);
            Assert.Equal(
                AppStrings.Format("DllInjectionMissingDllFormat", string.Empty),
                snapshot.EventMonitorSummary.FailureMessage);
            Assert.Null(snapshot.ReadResult);
            AssertCommandAvailability(
                snapshot,
                connectEnabled: true,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Connect_WhenCurrentGameChangesDuringInjection_StopsInjectedMonitorAndPublishesCleanupState()
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
            Assert.Equal(GameConnectionEventMonitorState.CleanupRequested, connectedSnapshot.EventMonitorSummary.State);
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
            List<GameConnectionSnapshot> publishedSnapshots = [];
            session.SnapshotChanged += (_, args) => publishedSnapshots.Add(args.Snapshot);

            NotSupportedException exception = Assert.Throws<NotSupportedException>(
                () => session.Connect());

            Assert.Equal("payload unavailable", exception.Message);
            Assert.Equal(2, publishedSnapshots.Count);
            Assert.Equal(GameConnectionPhase.Connecting, publishedSnapshots[0].ConnectionPhase);
            GameConnectionSnapshot statusSnapshot = session.Snapshot;
            Assert.Equal(statusSnapshot, publishedSnapshots[^1]);
            Assert.Equal(GameConnectionPhase.Detected, statusSnapshot.ConnectionPhase);
            AssertCommandAvailability(
                statusSnapshot,
                connectEnabled: true,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
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
            List<GameConnectionSnapshot> publishedSnapshots = [];
            session.SnapshotChanged += (_, args) => publishedSnapshots.Add(args.Snapshot);

            eventMonitor.ResetCalls();
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => session.Connect());

            Assert.Equal("read failed", exception.Message);
            Assert.Equal(2, publishedSnapshots.Count);
            Assert.Equal(GameConnectionPhase.Connecting, publishedSnapshots[0].ConnectionPhase);
            GameConnectionSnapshot statusSnapshot = session.Snapshot;
            Assert.Equal(statusSnapshot, publishedSnapshots[^1]);
            Assert.Equal(GameConnectionPhase.Detected, statusSnapshot.ConnectionPhase);
            AssertCommandAvailability(
                statusSnapshot,
                connectEnabled: true,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
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

            Assert.Equal(GameConnectionEventMonitorState.Ready, snapshot.EventMonitorSummary.State);
            Assert.Same(compatibleStatus, snapshot.EventMonitorSummary.Status);
            Assert.Equal(2, eventMonitor.ReadStatusCallCount);
            Assert.Equal(timeProvider.GetUtcNow(), eventMonitor.LastReceivedAt);
            Assert.Equal(1001, eventMonitor.LastTargetProcessId);
        }

        [Theory]
        [InlineData(GameCompatibilityState.UnsupportedVersion, GameConnectionEventMonitorState.UnsupportedVersion)]
        [InlineData(GameCompatibilityState.CaptureDisabled, GameConnectionEventMonitorState.CaptureDisabled)]
        [InlineData(GameCompatibilityState.PollingFallback, GameConnectionEventMonitorState.PollingFallback)]
        public void Read_WhenMonitorReportsCompatibilityState_ExposesEventMonitorSummary(
            GameCompatibilityState compatibilityState,
            GameConnectionEventMonitorState expectedState)
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            GameEventMonitorStatus monitorStatus = new(
                compatibilityState,
                DroppedEventCount: 1,
                DroppedNotifyCount: 2,
                PublishedNotifyCount: 3,
                RecentEvents:
                [
                    new GameEvent(
                        GameEventType.BoxEvent,
                        "randomization_done",
                        5,
                        10,
                        20,
                        new DateTimeOffset(2026, 5, 2, 12, 0, 0, TimeSpan.Zero),
                        "ray_gun_zm")
                ]);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = monitorStatus
            };
            GameConnectionSession session = CreateStartedSession(eventMonitor, detectedGame);
            CompleteConnectWithLoadedMonitor(session);

            GameConnectionSnapshot snapshot = session.Read();

            Assert.Equal(expectedState, snapshot.EventMonitorSummary.State);
            Assert.Same(monitorStatus, snapshot.EventMonitorSummary.Status);
            Assert.Same(monitorStatus.RecentEvents, snapshot.EventMonitorSummary.Status.RecentEvents);
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
            GameConnectionSnapshot cleanupSnapshot = context.Session.Snapshot;

            GameConnectionSnapshot snapshot = context.Session.Read();

            Assert.Equal(GameConnectionEventMonitorState.CleanupRequested, cleanupSnapshot.EventMonitorSummary.State);
            Assert.Same(detectedGame, snapshot.CurrentGame);
            Assert.Equal(GameConnectionEventMonitorState.Waiting, snapshot.EventMonitorSummary.State);
            Assert.Equal(GameConnectionPhase.Detected, snapshot.ConnectionPhase);
            Assert.Null(snapshot.ReadResult);
            Assert.Equal(attachCallCount, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(1, eventMonitor.RequestStopCallCount);
            Assert.Equal(1001, eventMonitor.LastStopTargetProcessId);
        }

        [Fact]
        public void Connect_WhenCurrentGameChangesDuringFirstConnectedStatsRead_ReturnsCurrentStatusWithoutReadingOldMonitor()
        {
            DetectedGame originalGame = CreateSupportedGame(processId: 1001);
            DetectedGame changedGame = CreateSupportedGame(processId: 2002);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            SessionContext? context = null;
            memoryAccessor.AttachCallback = (_, _) =>
            {
                context!.EventDetector.Result = changedGame;
                context.LifecycleEventSource.RaiseStarted(changedGame.ProcessName, changedGame.ProcessId);
            };
            context = CreateSessionContext(
                eventMonitor,
                detectedGame: originalGame,
                memoryAccessor: memoryAccessor);
            context.Session.Start();

            GameConnectionSnapshot snapshot = context.Session.Connect();

            Assert.Same(changedGame, snapshot.CurrentGame);
            Assert.Equal(GameConnectionPhase.Detected, snapshot.ConnectionPhase);
            Assert.Null(snapshot.ReadResult);
            Assert.Equal(GameConnectionEventMonitorState.Waiting, snapshot.EventMonitorSummary.State);
            AssertCommandAvailability(
                snapshot,
                connectEnabled: true,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
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
            List<GameConnectionSnapshot> publishedSnapshots = [];
            context.Session.SnapshotChanged += (_, args) => publishedSnapshots.Add(args.Snapshot);
            memoryAccessor.AttachCallback = (_, _) =>
            {
                context.EventDetector.Result = detectedGame;
                context.LifecycleEventSource.RaiseStarted(detectedGame.ProcessName, detectedGame.ProcessId);
            };

            GameConnectionSnapshot snapshot = context.Session.Read();

            Assert.Same(detectedGame, snapshot.CurrentGame);
            Assert.Null(snapshot.ReadResult);
            Assert.Contains(
                publishedSnapshots,
                publishedSnapshot => publishedSnapshot.EventMonitorSummary.State == GameConnectionEventMonitorState.CleanupRequested);
            Assert.Equal(GameConnectionEventMonitorState.Waiting, snapshot.EventMonitorSummary.State);
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
            FakeProcessMemoryAccessor memoryAccessor = new();
            GameConnectionSession session = CreateStartedSession(
                eventMonitor,
                detectedGame,
                timeProvider,
                memoryAccessor);
            CompleteConnectWithLoadedMonitor(session);
            int closeCallCount = memoryAccessor.CloseCallCount;
            eventMonitor.ResetCalls();

            timeProvider.Advance(TimeSpan.FromSeconds(16));
            GameConnectionSnapshot snapshot = session.Read();

            Assert.Equal(GameConnectionPhase.Detected, snapshot.ConnectionPhase);
            Assert.Null(snapshot.ReadResult);
            Assert.Equal(GameConnectionEventMonitorState.ReadinessFailed, snapshot.EventMonitorSummary.State);
            Assert.Equal(AppStrings.Get("DllInjectionReadinessTimedOut"), snapshot.EventMonitorSummary.FailureMessage);
            AssertCommandAvailability(
                snapshot,
                connectEnabled: true,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
            Assert.Equal(1, eventMonitor.RequestStopCallCount);
            Assert.Equal(1001, eventMonitor.LastStopTargetProcessId);
            Assert.Equal(closeCallCount + 1, memoryAccessor.CloseCallCount);
        }

        [Fact]
        public void Read_WhenMonitorReadinessTimesOutAndStatsReadFails_RequestsStopAndMarksInjectionFailed()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = GameEventMonitorStatus.WaitingForMonitor
            };
            var timeProvider = new FakeTimeProvider();
            FakeProcessMemoryAccessor memoryAccessor = new();
            GameConnectionSession session = CreateStartedSession(
                eventMonitor,
                detectedGame,
                timeProvider,
                memoryAccessor);
            CompleteConnectWithLoadedMonitor(session);
            memoryAccessor.AttachException = new InvalidOperationException("stats unavailable");
            eventMonitor.ResetCalls();

            timeProvider.Advance(TimeSpan.FromSeconds(16));
            Assert.Throws<InvalidOperationException>(() => session.Read());

            GameConnectionSnapshot snapshot = session.Snapshot;
            Assert.Equal(GameConnectionEventMonitorState.ReadinessFailed, snapshot.EventMonitorSummary.State);
            Assert.Equal(AppStrings.Get("DllInjectionReadinessTimedOut"), snapshot.EventMonitorSummary.FailureMessage);
            Assert.Null(snapshot.ReadResult);
            AssertCommandAvailability(
                snapshot,
                connectEnabled: true,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
            Assert.Equal(1, eventMonitor.ReadStatusCallCount);
            Assert.Equal(1001, eventMonitor.LastTargetProcessId);
            Assert.Equal(1, eventMonitor.RequestStopCallCount);
            Assert.Equal(1001, eventMonitor.LastStopTargetProcessId);

            eventMonitor.ResetCalls();
            GameConnectionSnapshot statusSnapshot = session.GetStatusSnapshot();

            Assert.Equal(GameConnectionPhase.Detected, statusSnapshot.ConnectionPhase);
            Assert.Equal(GameConnectionEventMonitorState.ReadinessFailed, statusSnapshot.EventMonitorSummary.State);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(0, eventMonitor.RequestStopCallCount);
        }

        [Fact]
        public void Read_WhenMonitorReadinessTimeoutElapsedForDifferentCurrentGame_DoesNotFireTimeout()
        {
            DetectedGame connectedGame = CreateSupportedGame(processId: 1001);
            DetectedGame detectedGame = CreateSupportedGame(processId: 2002);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = GameEventMonitorStatus.WaitingForMonitor
            };
            var timeProvider = new FakeTimeProvider();
            SessionContext context = CreateSessionContext(
                eventMonitor,
                timeProvider,
                detectedGame: connectedGame);
            context.Session.Start();
            CompleteConnectWithLoadedMonitor(context.Session);

            timeProvider.Advance(TimeSpan.FromSeconds(16));
            context.EventDetector.Result = detectedGame;
            context.LifecycleEventSource.RaiseStarted(detectedGame.ProcessName, detectedGame.ProcessId);
            eventMonitor.ResetCalls();
            GameConnectionSnapshot snapshot = context.Session.Read();

            Assert.Same(detectedGame, snapshot.CurrentGame);
            Assert.Equal(GameConnectionPhase.Detected, snapshot.ConnectionPhase);
            Assert.Equal(GameConnectionEventMonitorState.Waiting, snapshot.EventMonitorSummary.State);
            Assert.Null(snapshot.ReadResult);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(0, eventMonitor.RequestStopCallCount);
        }

        [Fact]
        public void Read_WhenMonitorIsReadyPastReadinessTimeout_DoesNotRequestStop()
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
            eventMonitor.ResetCalls();

            timeProvider.Advance(TimeSpan.FromSeconds(16));
            GameConnectionSnapshot snapshot = session.Read();

            Assert.Equal(GameConnectionPhase.Connected, snapshot.ConnectionPhase);
            Assert.Equal(GameConnectionEventMonitorState.Ready, snapshot.EventMonitorSummary.State);
            Assert.Same(compatibleStatus, snapshot.EventMonitorSummary.Status);
            Assert.Equal(1, eventMonitor.ReadStatusCallCount);
            Assert.Equal(0, eventMonitor.RequestStopCallCount);
        }

        [Fact]
        public void Disconnect_WhenMonitorIsConnected_RequestsStopAndReturnsDisconnectingSnapshot()
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
            int attachCallCount = memoryAccessor.AttachCallCount;
            int closeCallCount = memoryAccessor.CloseCallCount;
            eventMonitor.ResetCalls();

            List<GameConnectionSnapshotChangedEventArgs> changes = [];
            session.SnapshotChanged += (_, args) => changes.Add(args);

            GameConnectionSnapshot snapshot = session.Disconnect();

            Assert.Equal(GameConnectionPhase.Disconnecting, snapshot.ConnectionPhase);
            Assert.Equal(GameConnectionEventMonitorState.Disconnecting, snapshot.EventMonitorSummary.State);
            Assert.Same(GameEventMonitorStatus.WaitingForMonitor, snapshot.EventMonitorSummary.Status);
            Assert.Empty(snapshot.EventMonitorSummary.Status.RecentEvents);
            Assert.Equal(GameConnectionPhase.Disconnecting, session.GetStatusSnapshot().ConnectionPhase);
            AssertCommandAvailability(
                snapshot,
                connectEnabled: false,
                connectVisible: false,
                disconnectEnabled: false,
                disconnectVisible: false);
            Assert.Equal(snapshot, session.Snapshot);
            GameConnectionSnapshotChangedEventArgs change = Assert.Single(changes);
            Assert.Equal(GameConnectionPhase.Disconnecting, change.Snapshot.ConnectionPhase);
            Assert.Equal(snapshot, change.Snapshot);
            Assert.Equal(1, eventMonitor.RequestStopCallCount);
            Assert.Equal(1001, eventMonitor.LastStopTargetProcessId);
            Assert.Equal(0, eventMonitor.IsStopCompleteCallCount);
            Assert.Equal(attachCallCount, memoryAccessor.AttachCallCount);
            Assert.Equal(closeCallCount + 1, memoryAccessor.CloseCallCount);
        }

        [Fact]
        public void Disconnect_WhenAlreadyDisconnecting_DoesNotRequestDuplicateStop()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            GameConnectionSession session = CreateStartedSession(eventMonitor, detectedGame);
            CompleteConnectWithLoadedMonitor(session);

            GameConnectionSnapshot firstSnapshot = session.Disconnect();
            GameConnectionSnapshot secondSnapshot = session.Disconnect();

            Assert.Equal(GameConnectionPhase.Disconnecting, firstSnapshot.ConnectionPhase);
            Assert.Equal(GameConnectionPhase.Disconnecting, secondSnapshot.ConnectionPhase);
            Assert.Equal(GameConnectionEventMonitorState.Disconnecting, firstSnapshot.EventMonitorSummary.State);
            Assert.Equal(GameConnectionEventMonitorState.Disconnecting, secondSnapshot.EventMonitorSummary.State);
            Assert.Equal(secondSnapshot, session.Snapshot);
            Assert.Equal(1, eventMonitor.RequestStopCallCount);
            Assert.Equal(1001, eventMonitor.LastStopTargetProcessId);
            Assert.Equal(0, eventMonitor.IsStopCompleteCallCount);
        }

        [Fact]
        public void Disconnect_WhenNoOwnedMonitor_ReturnsSnapshotWithoutRequestingStop()
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

            GameConnectionSnapshot snapshot = session.Disconnect();

            Assert.Equal(GameConnectionPhase.Detected, snapshot.ConnectionPhase);
            AssertCommandAvailability(
                snapshot,
                connectEnabled: true,
                connectVisible: true,
                disconnectEnabled: false,
                disconnectVisible: false);
            Assert.Same(detectedGame, snapshot.CurrentGame);
            Assert.Null(snapshot.ReadResult);
            Assert.Equal(snapshot, session.Snapshot);
            Assert.Equal(0, eventMonitor.RequestStopCallCount);
            Assert.Equal(0, eventMonitor.IsStopCompleteCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
            Assert.Equal(0, memoryAccessor.AttachCallCount);
        }

        [Fact]
        public void Dispose_WhenMonitorIsConnected_RequestsStopForOwnedMonitor()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            using GameConnectionSession session = CreateStartedSession(eventMonitor, detectedGame);
            CompleteConnectWithLoadedMonitor(session);
            eventMonitor.ResetCalls();

            session.Dispose();

            Assert.Equal(1, eventMonitor.RequestStopCallCount);
            Assert.Equal(1001, eventMonitor.LastStopTargetProcessId);
            Assert.Equal(0, eventMonitor.IsStopCompleteCallCount);
        }

        [Fact]
        public void Dispose_WhenMonitorStopAlreadyRequested_DoesNotRequestDuplicateStop()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            using GameConnectionSession session = CreateStartedSession(eventMonitor, detectedGame);
            CompleteConnectWithLoadedMonitor(session);
            session.Disconnect();

            session.Dispose();

            Assert.Equal(1, eventMonitor.RequestStopCallCount);
            Assert.Equal(1001, eventMonitor.LastStopTargetProcessId);
            Assert.Equal(0, eventMonitor.IsStopCompleteCallCount);
        }

        [Fact]
        public void HandleReadFailure_WhenMonitorIsConnected_PreservesConnectedSnapshot()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            GameEventMonitorStatus compatibleStatus = CreateCompatibleStatus();
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = compatibleStatus
            };
            GameConnectionSession session = CreateStartedSession(eventMonitor, detectedGame);
            CompleteConnectWithLoadedMonitor(session);
            session.Disconnect();
            eventMonitor.ResetCalls();

            GameConnectionSnapshot cleanupSnapshot = session.HandleReadFailure();
            GameConnectionSnapshot readSnapshot = session.Read();

            Assert.Equal(GameConnectionPhase.Connected, cleanupSnapshot.ConnectionPhase);
            AssertCommandAvailability(
                cleanupSnapshot,
                connectEnabled: false,
                connectVisible: false,
                disconnectEnabled: true,
                disconnectVisible: true);
            Assert.Same(detectedGame, cleanupSnapshot.CurrentGame);
            Assert.Null(cleanupSnapshot.ReadResult);
            Assert.Same(compatibleStatus, readSnapshot.EventMonitorSummary.Status);
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
            FakeProcessMemoryAccessor memoryAccessor = new();
            var timeProvider = new FakeTimeProvider();
            GameConnectionSession session = CreateStartedSession(
                eventMonitor,
                detectedGame,
                timeProvider,
                memoryAccessor);
            CompleteConnectWithLoadedMonitor(session);
            session.Disconnect();
            int attachCallCount = memoryAccessor.AttachCallCount;
            eventMonitor.ResetCalls();

            GameConnectionSnapshot snapshot = session.Read();

            Assert.Equal(GameConnectionPhase.Disconnecting, snapshot.ConnectionPhase);
            Assert.Equal(GameConnectionEventMonitorState.StopPending, snapshot.EventMonitorSummary.State);
            Assert.Same(GameEventMonitorStatus.WaitingForMonitor, snapshot.EventMonitorSummary.Status);
            Assert.Empty(snapshot.EventMonitorSummary.Status.RecentEvents);
            Assert.Null(snapshot.ReadResult);
            Assert.Equal(GameConnectionPhase.Disconnecting, session.GetStatusSnapshot().ConnectionPhase);
            Assert.Equal(attachCallCount, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.RequestStopCallCount);
            Assert.Equal(1, eventMonitor.IsStopCompleteCallCount);
            Assert.Equal(1001, eventMonitor.LastStopCompleteTargetProcessId);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Read_WhenDisconnectStopCompletes_PublishesStopCompletedSnapshot()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                IsStopCompleteResult = true,
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            var timeProvider = new FakeTimeProvider();
            GameConnectionSession session = CreateStartedSession(
                eventMonitor,
                detectedGame,
                timeProvider,
                memoryAccessor);
            CompleteConnectWithLoadedMonitor(session);
            session.Disconnect();
            int attachCallCount = memoryAccessor.AttachCallCount;
            eventMonitor.ResetCalls();

            GameConnectionSnapshot snapshot = session.Read();

            Assert.Equal(GameConnectionEventMonitorState.StopCompleted, snapshot.EventMonitorSummary.State);
            Assert.Same(GameEventMonitorStatus.WaitingForMonitor, snapshot.EventMonitorSummary.Status);
            Assert.Null(snapshot.ReadResult);
            Assert.Equal(GameConnectionPhase.Detected, snapshot.ConnectionPhase);
            Assert.NotEqual(GameConnectionPhase.Disconnecting, session.GetStatusSnapshot().ConnectionPhase);
            Assert.Equal(attachCallCount, memoryAccessor.AttachCallCount);
            Assert.Equal(0, eventMonitor.RequestStopCallCount);
            Assert.Equal(1, eventMonitor.IsStopCompleteCallCount);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Read_WhenDisconnectStopTimesOut_PublishesStopTimedOutSnapshot()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                IsStopCompleteResult = false,
                Status = CreateCompatibleStatus()
            };
            FakeProcessMemoryAccessor memoryAccessor = new();
            var timeProvider = new FakeTimeProvider();
            GameConnectionSession session = CreateStartedSession(
                eventMonitor,
                detectedGame,
                timeProvider,
                memoryAccessor);
            CompleteConnectWithLoadedMonitor(session);
            session.Disconnect();
            int attachCallCount = memoryAccessor.AttachCallCount;
            eventMonitor.ResetCalls();

            timeProvider.Advance(TimeSpan.FromSeconds(3));
            GameConnectionSnapshot snapshot = session.Read();

            Assert.Equal(GameConnectionEventMonitorState.StopTimedOut, snapshot.EventMonitorSummary.State);
            Assert.Same(GameEventMonitorStatus.WaitingForMonitor, snapshot.EventMonitorSummary.Status);
            Assert.Null(snapshot.ReadResult);
            Assert.Equal(GameConnectionPhase.Detected, snapshot.ConnectionPhase);
            Assert.NotEqual(GameConnectionPhase.Disconnecting, session.GetStatusSnapshot().ConnectionPhase);
            Assert.Equal(attachCallCount, memoryAccessor.AttachCallCount);
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
            Assert.Equal(GameConnectionPhase.Connected, connectedSnapshot.ConnectionPhase);
            return connectedSnapshot;
        }

        private static GameEventMonitorStatus CreateCompatibleStatus()
        {
            return new GameEventMonitorStatus(
                GameCompatibilityState.Compatible,
                0,
                0,
                1,
                []);
        }

        private static void AssertCommandAvailability(
            GameConnectionSnapshot snapshot,
            bool connectEnabled,
            bool connectVisible,
            bool disconnectEnabled,
            bool disconnectVisible)
        {
            Assert.Equal(connectEnabled, snapshot.ConnectCommandAvailability.IsEnabled);
            Assert.Equal(connectVisible, snapshot.ConnectCommandAvailability.IsVisible);
            Assert.Equal(disconnectEnabled, snapshot.DisconnectCommandAvailability.IsEnabled);
            Assert.Equal(disconnectVisible, snapshot.DisconnectCommandAvailability.IsVisible);
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
