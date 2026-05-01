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
        public void Read_WhenNoOwnedMonitorProcessId_DoesNotReadEventMonitor()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            GameConnectionSession session = CreateSession(eventMonitor);

            GameConnectionRefreshResult snapshot = session.Read(detectedGame);

            Assert.Equal(GameCompatibilityState.WaitingForMonitor, snapshot.EventStatus.CompatibilityState);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Read_WhenNoDetectedGame_DoesNotReadEventMonitor()
        {
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            GameConnectionSession session = CreateSession(eventMonitor);

            GameConnectionRefreshResult snapshot = session.Read(detectedGame: null);

            Assert.Equal(GameCompatibilityState.WaitingForMonitor, snapshot.EventStatus.CompatibilityState);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Read_WhenMonitorIsConnectedForDetectedGame_ReadsEventMonitor()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            GameEventMonitorStatus compatibleStatus = CreateCompatibleStatus();
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = compatibleStatus
            };
            var timeProvider = new FakeTimeProvider();
            GameConnectionSession session = CreateSession(eventMonitor, timeProvider);
            session.CompleteConnect(detectedGame, CreateLoadedInjectionResult());

            GameConnectionRefreshResult snapshot = session.Read(detectedGame);

            Assert.Same(compatibleStatus, snapshot.EventStatus);
            Assert.Equal(2, eventMonitor.ReadStatusCallCount);
            Assert.Equal(timeProvider.GetUtcNow(), eventMonitor.LastReceivedAt);
            Assert.Equal(1001, eventMonitor.LastTargetProcessId);
        }

        [Fact]
        public void Read_WhenConnectedMonitorProcessDiffers_DoesNotReadEventMonitor()
        {
            DetectedGame connectedGame = CreateSupportedGame(processId: 1001);
            DetectedGame detectedGame = CreateSupportedGame(processId: 2002);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            GameConnectionSession session = CreateSession(eventMonitor);
            session.CompleteConnect(connectedGame, CreateLoadedInjectionResult());
            eventMonitor.ResetCalls();

            GameConnectionRefreshResult snapshot = session.Read(detectedGame);

            Assert.Equal(GameCompatibilityState.WaitingForMonitor, snapshot.EventStatus.CompatibilityState);
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
            GameConnectionSession session = CreateSession(eventMonitor, timeProvider);
            session.CompleteConnect(detectedGame, CreateLoadedInjectionResult());
            eventMonitor.ResetCalls();

            timeProvider.Advance(TimeSpan.FromSeconds(16));
            GameConnectionRefreshResult snapshot = session.Read(detectedGame);

            Assert.Equal(DllInjectionState.Failed, snapshot.InjectionResult.State);
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
            GameConnectionSession session = CreateSession(eventMonitor);
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
            GameConnectionSession session = CreateSession(eventMonitor);
            session.CompleteConnect(detectedGame, CreateLoadedInjectionResult());
            session.BeginConnect();
            eventMonitor.ResetCalls();

            session.ClearTransientOperationState();
            GameConnectionRefreshResult snapshot = session.Read(detectedGame);

            Assert.False(session.IsConnecting);
            Assert.False(session.IsDisconnecting);
            Assert.Same(compatibleStatus, snapshot.EventStatus);
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
            GameConnectionSession session = CreateSession(eventMonitor, timeProvider);
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
            GameConnectionSession session = CreateSession(eventMonitor, timeProvider);
            session.CompleteConnect(detectedGame, CreateLoadedInjectionResult());
            session.TryBeginDisconnect();

            timeProvider.Advance(TimeSpan.FromSeconds(3));

            Assert.True(session.IsMonitorDisconnectComplete());
        }

        private static GameConnectionSession CreateSession(
            FakeGameEventMonitor eventMonitor,
            TimeProvider? timeProvider = null)
        {
            GameSessionCoordinator coordinator = new(
                new GameMemoryReader(new FakeProcessMemoryAccessor()),
                new GameProcessDetectionService(new FakeGameProcessDetector(), new FakeProcessLifecycleEventSource()),
                new FakeGameProcessDetector(),
                CreateDllInjector(),
                eventMonitor);
            return new GameConnectionSession(coordinator, timeProvider ?? TimeProvider.System);
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

            public void Start()
            {
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
