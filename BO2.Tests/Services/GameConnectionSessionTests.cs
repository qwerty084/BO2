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
            session.CompleteConnect(detectedGame, CreateLoadedInjectionResult());

            GameConnectionRefreshResult snapshot = session.Read();

            Assert.Same(compatibleStatus, snapshot.EventStatus);
            Assert.True(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(2, eventMonitor.ReadStatusCallCount);
            Assert.Equal(timeProvider.GetUtcNow(), eventMonitor.LastReceivedAt);
            Assert.Equal(1001, eventMonitor.LastTargetProcessId);
        }

        [Fact]
        public void Read_WhenCurrentGameChangesAfterConnect_DoesNotReadEventMonitor()
        {
            DetectedGame connectedGame = CreateSupportedGame(processId: 1001);
            DetectedGame detectedGame = CreateSupportedGame(processId: 2002);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            SessionContext context = CreateSessionContext(eventMonitor, detectedGame: connectedGame);
            context.Session.Start();
            context.Session.CompleteConnect(connectedGame, CreateLoadedInjectionResult());
            context.EventDetector.Result = detectedGame;
            context.LifecycleEventSource.RaiseStarted(detectedGame.ProcessName, detectedGame.ProcessId);
            eventMonitor.ResetCalls();

            GameConnectionRefreshResult snapshot = context.Session.Read();

            Assert.Same(detectedGame, snapshot.CurrentGame);
            Assert.Equal(GameCompatibilityState.WaitingForMonitor, snapshot.EventStatus.CompatibilityState);
            Assert.False(snapshot.IsMonitorConnectedForCurrentGame);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
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
            session.CompleteConnect(detectedGame, CreateLoadedInjectionResult());
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
            session.CompleteConnect(detectedGame, CreateLoadedInjectionResult());
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
            session.CompleteConnect(detectedGame, CreateLoadedInjectionResult());
            session.BeginConnect();
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
            session.CompleteConnect(detectedGame, CreateLoadedInjectionResult());
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
            session.CompleteConnect(detectedGame, CreateLoadedInjectionResult());
            session.TryBeginDisconnect();

            timeProvider.Advance(TimeSpan.FromSeconds(3));

            Assert.True(session.IsMonitorDisconnectComplete());
        }

        private static GameConnectionSession CreateStartedSession(
            FakeGameEventMonitor eventMonitor,
            DetectedGame? detectedGame = null,
            TimeProvider? timeProvider = null,
            FakeProcessMemoryAccessor? memoryAccessor = null)
        {
            SessionContext context = CreateSessionContext(
                eventMonitor,
                timeProvider,
                detectedGame,
                memoryAccessor: memoryAccessor);
            context.Session.Start();
            return context.Session;
        }

        private static SessionContext CreateSessionContext(
            FakeGameEventMonitor eventMonitor,
            TimeProvider? timeProvider = null,
            DetectedGame? detectedGame = null,
            DetectedGame? pollingDetectedGame = null,
            FakeProcessMemoryAccessor? memoryAccessor = null,
            FakeProcessLifecycleEventSource? lifecycleEventSource = null)
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
                CreateDllInjector(),
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

        private static DllInjector CreateDllInjector()
        {
            return new DllInjector(
                () => false,
                () => string.Empty,
                _ => false,
                _ => DllInjector.DllPayloadValidationResult.Valid,
                _ => false,
                (_, _) => { },
                (_, _) => { });
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
