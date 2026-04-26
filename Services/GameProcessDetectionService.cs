using System;

namespace BO2.Services
{
    internal sealed class GameProcessDetectionService : IDisposable
    {
        private readonly object _syncRoot = new();
        private readonly IGameProcessDetector _gameProcessDetector;
        private readonly IProcessLifecycleEventSource _processLifecycleEventSource;
        private DetectedGame? _currentGame;
        private bool _started;
        private bool _disposed;

        public GameProcessDetectionService()
            : this(new GameProcessDetector(), new WindowsProcessLifecycleEventSource())
        {
        }

        internal GameProcessDetectionService(
            IGameProcessDetector gameProcessDetector,
            IProcessLifecycleEventSource processLifecycleEventSource)
        {
            ArgumentNullException.ThrowIfNull(gameProcessDetector);
            ArgumentNullException.ThrowIfNull(processLifecycleEventSource);

            _gameProcessDetector = gameProcessDetector;
            _processLifecycleEventSource = processLifecycleEventSource;
        }

        public event EventHandler<DetectedGameChangedEventArgs>? DetectedGameChanged;

        public DetectedGame? CurrentGame
        {
            get
            {
                lock (_syncRoot)
                {
                    return _currentGame;
                }
            }
        }

        public void Start()
        {
            lock (_syncRoot)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_started)
                {
                    return;
                }

                _started = true;
                _processLifecycleEventSource.ProcessStarted += OnProcessLifecycleChanged;
                _processLifecycleEventSource.ProcessStopped += OnProcessLifecycleChanged;
            }

            RefreshDetection();
            try
            {
                _processLifecycleEventSource.Start();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _processLifecycleEventSource.ProcessStarted -= OnProcessLifecycleChanged;
                _processLifecycleEventSource.ProcessStopped -= OnProcessLifecycleChanged;
            }

            _processLifecycleEventSource.Dispose();
        }

        private void OnProcessLifecycleChanged(object? sender, ProcessLifecycleEventArgs args)
        {
            if (!GameProcessDetector.IsKnownProcessName(args.ProcessName))
            {
                return;
            }

            RefreshDetection();
        }

        private void RefreshDetection()
        {
            DetectedGame? detectedGame = _gameProcessDetector.Detect();
            EventHandler<DetectedGameChangedEventArgs>? handler;

            lock (_syncRoot)
            {
                if (_disposed || Equals(_currentGame, detectedGame))
                {
                    return;
                }

                _currentGame = detectedGame;
                handler = DetectedGameChanged;
            }

            handler?.Invoke(this, new DetectedGameChangedEventArgs(detectedGame));
        }
    }
}
