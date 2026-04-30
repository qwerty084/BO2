using System;
using System.Management;
using System.Runtime.InteropServices;

namespace BO2.Services
{
    internal sealed class GameSessionCoordinator : IDisposable
    {
        private readonly GameMemoryReader _memoryReader;
        private readonly GameProcessDetectionService _processDetectionService;
        private readonly IGameProcessDetector _pollingProcessDetector;
        private readonly DllInjector _dllInjector;
        private readonly IGameEventMonitor _eventMonitor;

        public GameSessionCoordinator()
            : this(
                new GameMemoryReader(),
                new GameProcessDetectionService(),
                new GameProcessDetector(),
                new DllInjector(),
                new GameEventMonitor())
        {
        }

        internal GameSessionCoordinator(
            GameMemoryReader memoryReader,
            GameProcessDetectionService processDetectionService,
            IGameProcessDetector pollingProcessDetector,
            DllInjector dllInjector,
            IGameEventMonitor eventMonitor)
        {
            ArgumentNullException.ThrowIfNull(memoryReader);
            ArgumentNullException.ThrowIfNull(processDetectionService);
            ArgumentNullException.ThrowIfNull(pollingProcessDetector);
            ArgumentNullException.ThrowIfNull(dllInjector);
            ArgumentNullException.ThrowIfNull(eventMonitor);

            _memoryReader = memoryReader;
            _processDetectionService = processDetectionService;
            _pollingProcessDetector = pollingProcessDetector;
            _dllInjector = dllInjector;
            _eventMonitor = eventMonitor;
        }

        public event EventHandler<DetectedGameChangedEventArgs>? DetectedGameChanged
        {
            add => _processDetectionService.DetectedGameChanged += value;
            remove => _processDetectionService.DetectedGameChanged -= value;
        }

        public bool UsesPollingProcessDetection { get; private set; }

        public DetectedGame? CurrentGame => _processDetectionService.CurrentGame;

        public void Start()
        {
            try
            {
                _processDetectionService.Start();
            }
            catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
            {
                UsesPollingProcessDetection = true;
            }
        }

        public DetectedGame? DetectByPolling()
        {
            long diagnosticsStartedAt = RefreshDiagnostics.Start();
            try
            {
                return _pollingProcessDetector.Detect();
            }
            finally
            {
                RefreshDiagnostics.WriteElapsed("process polling detect", diagnosticsStartedAt);
            }
        }

        public PlayerStatsReadResult ReadPlayerStats(DetectedGame? detectedGame)
        {
            return _memoryReader.ReadPlayerStats(detectedGame);
        }

        public DllInjectionResult Inject(DetectedGame? detectedGame)
        {
            return _dllInjector.Inject(detectedGame);
        }

        public GameEventMonitorStatus ReadEventMonitorStatus(DateTimeOffset receivedAt, int? targetProcessId)
        {
            return _eventMonitor.ReadStatus(receivedAt, targetProcessId);
        }

        public void RequestMonitorStop(int? targetProcessId)
        {
            _eventMonitor.RequestStop(targetProcessId);
        }

        public bool IsMonitorStopComplete(int targetProcessId)
        {
            return _eventMonitor.IsStopComplete(targetProcessId);
        }

        public void Dispose()
        {
            _processDetectionService.Dispose();
            _eventMonitor.Dispose();
            _memoryReader.Dispose();
        }
    }
}
