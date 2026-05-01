using System;
using BO2.Services;
using BO2.Tests.Fakes;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameConnectionSessionTests
    {
        [Fact]
        public void Read_WhenNoCurrentGame_ReturnsNoGameSnapshot()
        {
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            GameConnectionSession session = CreateStartedSession(eventMonitor);

            GameConnectionRefreshResult snapshot = session.Read();

            Assert.Null(snapshot.CurrentGame);
            Assert.Null(snapshot.ReadResult.DetectedGame);
            Assert.Equal(ConnectionState.Disconnected, snapshot.ReadResult.ConnectionState);
            Assert.Equal(GameCompatibilityState.WaitingForMonitor, snapshot.EventStatus.CompatibilityState);
            Assert.False(snapshot.CanAttemptConnect);
            Assert.False(snapshot.HasInjectionAttemptForCurrentGame);
            Assert.False(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
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

            GameConnectionRefreshResult snapshot = session.Read();

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

            GameConnectionRefreshResult snapshot = session.Read();

            Assert.Same(detectedGame, snapshot.CurrentGame);
            Assert.Same(detectedGame, snapshot.ReadResult.DetectedGame);
            Assert.Null(snapshot.ReadResult.Stats);
            Assert.Equal(ConnectionState.Unsupported, snapshot.ReadResult.ConnectionState);
            Assert.Equal(GameCompatibilityState.WaitingForMonitor, snapshot.EventStatus.CompatibilityState);
            Assert.False(snapshot.CanAttemptConnect);
            Assert.False(snapshot.HasInjectionAttemptForCurrentGame);
            Assert.False(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
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

            GameConnectionRefreshResult snapshot = context.Session.Read();

            Assert.True(context.Session.UsesPollingProcessDetection);
            Assert.Same(detectedGame, context.Session.CurrentGame);
            Assert.Same(detectedGame, snapshot.CurrentGame);
            Assert.Same(detectedGame, snapshot.ReadResult.DetectedGame);
            Assert.NotNull(snapshot.ReadResult.Stats);
            Assert.Equal(1, context.PollingProcessDetector.DetectCallCount);
            Assert.Equal(1, memoryAccessor.AttachCallCount);
            Assert.True(snapshot.CanAttemptConnect);
        }

        [Fact]
        public void Connect_WhenCurrentGameSupported_InjectsCurrentGameAndRecordsMonitorOwnership()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            int? injectedProcessId = null;
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            DllInjector dllInjector = CreateDllInjector(
                fileExists: _ => true,
                injectLibrary: (processId, _) => injectedProcessId = processId);
            GameConnectionSession session = CreateStartedSession(eventMonitor, detectedGame, dllInjector: dllInjector);

            GameConnectionRefreshResult connectingSnapshot = session.BeginConnect();
            DllInjectionResult injectionResult = session.Inject();
            GameConnectionRefreshResult connectedSnapshot = session.CompleteConnect(injectionResult);

            Assert.True(connectingSnapshot.IsConnecting);
            Assert.False(connectingSnapshot.CanAttemptConnect);
            Assert.Equal(DllInjectionState.Loaded, injectionResult.State);
            Assert.Equal(1001, injectedProcessId);
            Assert.False(connectedSnapshot.IsConnecting);
            Assert.Same(detectedGame, connectedSnapshot.CurrentGame);
            Assert.Equal(DllInjectionState.Loaded, connectedSnapshot.InjectionResult.State);
            Assert.True(connectedSnapshot.IsMonitorConnectedForCurrentGame);
            Assert.True(session.IsMonitorConnectedFor(detectedGame));
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
            GameConnectionSession session = CreateStartedSession(eventMonitor, detectedGame);

            GameConnectionRefreshResult connectingSnapshot = session.BeginConnect();
            DllInjectionResult injectionResult = session.Inject();

            Assert.False(connectingSnapshot.IsConnecting);
            Assert.False(session.IsConnecting);
            Assert.Equal(DllInjectionState.NotAttempted, injectionResult.State);
            Assert.Equal(DllInjectionState.NotAttempted, connectingSnapshot.InjectionResult.State);
            Assert.False(connectingSnapshot.CanAttemptConnect);
            Assert.False(connectingSnapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Connect_WhenNoCurrentGame_DoesNotAttemptInjection()
        {
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            GameConnectionSession session = CreateStartedSession(eventMonitor);

            GameConnectionRefreshResult connectingSnapshot = session.BeginConnect();
            DllInjectionResult injectionResult = session.Inject();

            Assert.False(connectingSnapshot.IsConnecting);
            Assert.False(session.IsConnecting);
            Assert.Null(connectingSnapshot.CurrentGame);
            Assert.Equal(DllInjectionState.NotAttempted, injectionResult.State);
            Assert.False(connectingSnapshot.CanAttemptConnect);
            Assert.False(connectingSnapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Connect_WhenCurrentGameChangesDuringInjection_DoesNotRecordStaleMonitorOwnership()
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

            GameConnectionRefreshResult connectingSnapshot = context.Session.BeginConnect();
            DllInjectionResult injectionResult = context.Session.Inject();
            GameConnectionRefreshResult connectedSnapshot = context.Session.CompleteConnect(injectionResult);

            Assert.True(connectingSnapshot.IsConnecting);
            Assert.Equal(DllInjectionState.Loaded, injectionResult.State);
            Assert.Equal(1001, injectedProcessId);
            Assert.Same(changedGame, connectedSnapshot.CurrentGame);
            Assert.Equal(DllInjectionState.NotAttempted, connectedSnapshot.InjectionResult.State);
            Assert.False(connectedSnapshot.HasInjectionAttemptForCurrentGame);
            Assert.False(connectedSnapshot.IsMonitorConnectedForCurrentGame);
            Assert.False(context.Session.IsMonitorConnectedFor(originalGame));
            Assert.False(context.Session.IsMonitorConnectedFor(changedGame));
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
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

            GameConnectionRefreshResult snapshot = session.Read();

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
            SessionContext context = CreateSessionContext(eventMonitor, detectedGame: connectedGame);
            context.Session.Start();
            CompleteConnectWithLoadedMonitor(context.Session);
            eventMonitor.ResetCalls();
            context.EventDetector.Result = detectedGame;
            context.LifecycleEventSource.RaiseStarted(detectedGame.ProcessName, detectedGame.ProcessId);

            GameConnectionRefreshResult snapshot = context.Session.Read();

            Assert.Same(detectedGame, snapshot.CurrentGame);
            Assert.Equal(DllInjectionState.NotAttempted, snapshot.InjectionResult.State);
            Assert.Equal(GameCompatibilityState.WaitingForMonitor, snapshot.EventStatus.CompatibilityState);
            Assert.False(snapshot.HasInjectionAttemptForCurrentGame);
            Assert.False(snapshot.IsMonitorConnectedForCurrentGame);
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
            GameConnectionRefreshResult snapshot = session.Read();

            Assert.Equal(DllInjectionState.Failed, snapshot.InjectionResult.State);
            Assert.False(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(1, eventMonitor.RequestStopCallCount);
            Assert.Equal(1001, eventMonitor.LastStopTargetProcessId);
        }

        [Fact]
        public void TryBeginDisconnect_WhenMonitorIsConnected_RequestsStop()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            GameConnectionSession session = CreateStartedSession(eventMonitor, detectedGame);
            CompleteConnectWithLoadedMonitor(session);
            eventMonitor.ResetCalls();

            bool beganDisconnect = session.TryBeginDisconnect();

            Assert.True(beganDisconnect);
            Assert.True(session.IsDisconnecting);
            Assert.Equal(1, eventMonitor.RequestStopCallCount);
            Assert.Equal(1001, eventMonitor.LastStopTargetProcessId);
        }

        [Fact]
        public void ClearTransientOperationState_WhenMonitorIsConnected_PreservesMonitorOwnership()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            GameEventMonitorStatus compatibleStatus = CreateCompatibleStatus();
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = compatibleStatus
            };
            GameConnectionSession session = CreateStartedSession(eventMonitor, detectedGame);
            CompleteConnectWithLoadedMonitor(session);
            session.TryBeginDisconnect();
            eventMonitor.ResetCalls();

            session.ClearTransientOperationState();
            GameConnectionRefreshResult snapshot = session.Read();

            Assert.False(session.IsConnecting);
            Assert.False(session.IsDisconnecting);
            Assert.Same(compatibleStatus, snapshot.EventStatus);
            Assert.True(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.True(session.IsMonitorConnectedFor(detectedGame));
            Assert.Equal(1, eventMonitor.ReadStatusCallCount);
            Assert.Equal(0, eventMonitor.RequestStopCallCount);
        }

        [Fact]
        public void IsMonitorDisconnectComplete_WhenStopDoesNotCompleteBeforeTimeout_ReturnsFalse()
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
            session.TryBeginDisconnect();

            Assert.False(session.IsMonitorDisconnectComplete());
        }

        [Fact]
        public void IsMonitorDisconnectComplete_WhenStopDoesNotCompleteAfterTimeout_ReturnsTrue()
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
            session.TryBeginDisconnect();

            timeProvider.Advance(TimeSpan.FromSeconds(3));

            Assert.True(session.IsMonitorDisconnectComplete());
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

            GameSessionCoordinator coordinator = new(
                new GameMemoryReader(memoryAccessor),
                new GameProcessDetectionService(eventDetector, lifecycleEventSource),
                pollingProcessDetector,
                dllInjector ?? CreateDllInjector(),
                eventMonitor);
            GameConnectionSession session = new(coordinator, timeProvider ?? TimeProvider.System);
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
                fileExists ?? (_ => false),
                _ => DllInjector.DllPayloadValidationResult.Valid,
                _ => false,
                injectLibrary ?? ((_, _) => { }),
                (_, _) => { });
        }

        private static GameConnectionRefreshResult CompleteConnectWithLoadedMonitor(GameConnectionSession session)
        {
            GameConnectionRefreshResult connectingSnapshot = session.BeginConnect();
            Assert.True(connectingSnapshot.IsConnecting);
            return session.CompleteConnect(CreateLoadedInjectionResult());
        }

        private static DllInjectionResult CreateLoadedInjectionResult()
        {
            return new DllInjectionResult(DllInjectionState.Loaded, "Loaded", @"C:\app\BO2Monitor.dll");
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

            public DateTimeOffset? LastReceivedAt { get; private set; }

            public int? LastTargetProcessId { get; private set; }

            public int? LastStopTargetProcessId { get; private set; }

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
                return IsStopCompleteResult;
            }

            public void ResetCalls()
            {
                ReadStatusCallCount = 0;
                RequestStopCallCount = 0;
                LastReceivedAt = null;
                LastTargetProcessId = null;
                LastStopTargetProcessId = null;
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
