using System;
using BO2.Services;
using BO2.Tests.Fakes;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class StatsRefreshServiceTests
    {
        [Fact]
        public void Read_WhenNoOwnedMonitorProcessId_DoesNotReadEventMonitor()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            StatsRefreshService service = CreateService(eventMonitor);

            StatsRefreshSnapshot snapshot = service.Read(
                detectedGame,
                DateTimeOffset.UtcNow,
                ownedMonitorProcessId: null);

            Assert.Equal(GameCompatibilityState.WaitingForMonitor, snapshot.EventStatus.CompatibilityState);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Read_WhenNoDetectedGameAndNoOwnedMonitorProcessId_DoesNotReadEventMonitor()
        {
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            StatsRefreshService service = CreateService(eventMonitor);

            StatsRefreshSnapshot snapshot = service.Read(
                detectedGame: null,
                DateTimeOffset.UtcNow,
                ownedMonitorProcessId: null);

            Assert.Equal(GameCompatibilityState.WaitingForMonitor, snapshot.EventStatus.CompatibilityState);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        [Fact]
        public void Read_WhenOwnedMonitorProcessIdMatches_ReadsEventMonitor()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            GameEventMonitorStatus compatibleStatus = CreateCompatibleStatus();
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = compatibleStatus
            };
            StatsRefreshService service = CreateService(eventMonitor);
            DateTimeOffset receivedAt = DateTimeOffset.UtcNow;

            StatsRefreshSnapshot snapshot = service.Read(
                detectedGame,
                receivedAt,
                ownedMonitorProcessId: 1001);

            Assert.Same(compatibleStatus, snapshot.EventStatus);
            Assert.Equal(1, eventMonitor.ReadStatusCallCount);
            Assert.Equal(receivedAt, eventMonitor.LastReceivedAt);
            Assert.Equal(1001, eventMonitor.LastTargetProcessId);
        }

        [Fact]
        public void Read_WhenOwnedMonitorProcessIdDiffers_DoesNotReadEventMonitor()
        {
            DetectedGame detectedGame = CreateSupportedGame(processId: 1001);
            FakeGameEventMonitor eventMonitor = new()
            {
                Status = CreateCompatibleStatus()
            };
            StatsRefreshService service = CreateService(eventMonitor);

            StatsRefreshSnapshot snapshot = service.Read(
                detectedGame,
                DateTimeOffset.UtcNow,
                ownedMonitorProcessId: 2002);

            Assert.Equal(GameCompatibilityState.WaitingForMonitor, snapshot.EventStatus.CompatibilityState);
            Assert.Equal(0, eventMonitor.ReadStatusCallCount);
        }

        private static StatsRefreshService CreateService(FakeGameEventMonitor eventMonitor)
        {
            GameSessionCoordinator coordinator = new(
                new GameMemoryReader(new FakeProcessMemoryAccessor()),
                new GameProcessDetectionService(new FakeGameProcessDetector(), new FakeProcessLifecycleEventSource()),
                new FakeGameProcessDetector(),
                CreateDllInjector(),
                eventMonitor);
            return new StatsRefreshService(coordinator);
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

            public int ReadStatusCallCount { get; private set; }

            public DateTimeOffset? LastReceivedAt { get; private set; }

            public int? LastTargetProcessId { get; private set; }

            public GameEventMonitorStatus ReadStatus(DateTimeOffset receivedAt, int? targetProcessId)
            {
                ReadStatusCallCount++;
                LastReceivedAt = receivedAt;
                LastTargetProcessId = targetProcessId;
                return Status;
            }

            public void RequestStop(int? targetProcessId)
            {
            }

            public bool IsStopComplete(int targetProcessId)
            {
                return true;
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
