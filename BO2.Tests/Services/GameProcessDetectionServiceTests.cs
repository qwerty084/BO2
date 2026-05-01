using System;
using BO2.Services;
using BO2.Tests.Fakes;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameProcessDetectionServiceTests
    {
        [Fact]
        public void Start_PerformsInitialScan()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);
            var detector = new FakeGameProcessDetector { Result = detectedGame };
            var eventSource = new FakeProcessLifecycleEventSource();
            using var service = new GameProcessDetectionService(detector, eventSource);

            service.Start();

            Assert.Equal(detectedGame, service.CurrentGame);
            Assert.Equal(1, detector.DetectCallCount);
            Assert.Equal(1, eventSource.StartCallCount);
        }

        [Fact]
        public void ProcessStartEvent_ForKnownProcess_ReScansAndRaisesChanged()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);
            var detector = new FakeGameProcessDetector();
            var eventSource = new FakeProcessLifecycleEventSource();
            using var service = new GameProcessDetectionService(detector, eventSource);
            service.Start();
            DetectedGame? changedGame = null;
            service.DetectedGameChanged += (_, args) => changedGame = args.DetectedGame;

            detector.Result = detectedGame;
            eventSource.RaiseStarted("t6zm.exe", 1001);

            Assert.Equal(detectedGame, service.CurrentGame);
            Assert.Equal(detectedGame, changedGame);
            Assert.Equal(2, detector.DetectCallCount);
        }

        [Fact]
        public void ProcessStopEvent_ForKnownProcess_ReScansAndRaisesChanged()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);
            var detector = new FakeGameProcessDetector { Result = detectedGame };
            var eventSource = new FakeProcessLifecycleEventSource();
            using var service = new GameProcessDetectionService(detector, eventSource);
            service.Start();
            DetectedGame? changedGame = detectedGame;
            service.DetectedGameChanged += (_, args) => changedGame = args.DetectedGame;

            detector.Result = null;
            eventSource.RaiseStopped("t6zm.exe", 1001);

            Assert.Null(service.CurrentGame);
            Assert.Null(changedGame);
            Assert.Equal(2, detector.DetectCallCount);
        }

        [Fact]
        public void ProcessEvent_WhenDetectionIsUnchanged_DoesNotRaiseChanged()
        {
            DetectedGame detectedGame = CreateSupportedGame(1001);
            var detector = new FakeGameProcessDetector { Result = detectedGame };
            var eventSource = new FakeProcessLifecycleEventSource();
            using var service = new GameProcessDetectionService(detector, eventSource);
            service.Start();
            int changedCount = 0;
            service.DetectedGameChanged += (_, _) => changedCount++;

            eventSource.RaiseStarted("t6zm.exe", 1001);

            Assert.Equal(detectedGame, service.CurrentGame);
            Assert.Equal(0, changedCount);
            Assert.Equal(2, detector.DetectCallCount);
        }

        [Fact]
        public void ProcessEvent_ForUnknownProcess_DoesNotRescan()
        {
            var detector = new FakeGameProcessDetector();
            var eventSource = new FakeProcessLifecycleEventSource();
            using var service = new GameProcessDetectionService(detector, eventSource);
            service.Start();

            eventSource.RaiseStarted("notepad.exe", 1001);

            Assert.Equal(1, detector.DetectCallCount);
        }

        [Fact]
        public void Dispose_UnsubscribesFromLifecycleEvents()
        {
            var detector = new FakeGameProcessDetector();
            var eventSource = new FakeProcessLifecycleEventSource();
            using var service = new GameProcessDetectionService(detector, eventSource);
            service.Start();

            service.Dispose();
            detector.Result = CreateSupportedGame(1001);
            eventSource.RaiseStarted("t6zm.exe", 1001);

            Assert.Equal(1, detector.DetectCallCount);
            Assert.Equal(1, eventSource.DisposeCallCount);
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

        private sealed class FakeProcessLifecycleEventSource : IProcessLifecycleEventSource
        {
            public event EventHandler<ProcessLifecycleEventArgs>? ProcessStarted;

            public event EventHandler<ProcessLifecycleEventArgs>? ProcessStopped;

            public int StartCallCount { get; private set; }

            public int DisposeCallCount { get; private set; }

            public void Start()
            {
                StartCallCount++;
            }

            public void Dispose()
            {
                DisposeCallCount++;
            }

            public void RaiseStarted(string processName, int processId)
            {
                ProcessStarted?.Invoke(this, new ProcessLifecycleEventArgs(processName, processId));
            }

            public void RaiseStopped(string processName, int processId)
            {
                ProcessStopped?.Invoke(this, new ProcessLifecycleEventArgs(processName, processId));
            }
        }
    }
}
