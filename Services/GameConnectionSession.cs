using System;
using System.Management;
using System.Runtime.InteropServices;

namespace BO2.Services
{
    internal sealed class GameConnectionSession : IDisposable
    {
        private static readonly TimeSpan MonitorReadinessRetryTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan MonitorDisconnectTimeout = TimeSpan.FromSeconds(3);

        private readonly object _syncRoot = new();
        private readonly GameMemoryReader _memoryReader;
        private readonly GameProcessDetectionService _processDetectionService;
        private readonly IGameProcessDetector _pollingProcessDetector;
        private readonly DllInjector _dllInjector;
        private readonly IGameEventMonitor _eventMonitor;
        private readonly TimeProvider _timeProvider;
        private readonly GameConnectionSessionLifecycle _lifecycle = new();
        private DetectedGame? _currentGame;
        private bool _usesPollingProcessDetection;

        public GameConnectionSession()
            : this(
                new GameMemoryReader(),
                new GameProcessDetectionService(),
                new GameProcessDetector(),
                new DllInjector(),
                new GameEventMonitor(),
                TimeProvider.System)
        {
        }

        internal GameConnectionSession(
            GameMemoryReader memoryReader,
            GameProcessDetectionService processDetectionService,
            IGameProcessDetector pollingProcessDetector,
            DllInjector dllInjector,
            IGameEventMonitor eventMonitor)
            : this(
                memoryReader,
                processDetectionService,
                pollingProcessDetector,
                dllInjector,
                eventMonitor,
                TimeProvider.System)
        {
        }

        internal GameConnectionSession(
            GameMemoryReader memoryReader,
            GameProcessDetectionService processDetectionService,
            IGameProcessDetector pollingProcessDetector,
            DllInjector dllInjector,
            IGameEventMonitor eventMonitor,
            TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(memoryReader);
            ArgumentNullException.ThrowIfNull(processDetectionService);
            ArgumentNullException.ThrowIfNull(pollingProcessDetector);
            ArgumentNullException.ThrowIfNull(dllInjector);
            ArgumentNullException.ThrowIfNull(eventMonitor);
            ArgumentNullException.ThrowIfNull(timeProvider);

            _memoryReader = memoryReader;
            _processDetectionService = processDetectionService;
            _pollingProcessDetector = pollingProcessDetector;
            _dllInjector = dllInjector;
            _eventMonitor = eventMonitor;
            _timeProvider = timeProvider;
            _currentGame = _processDetectionService.CurrentGame;
            _processDetectionService.DetectedGameChanged += OnDetectedGameChanged;
        }

        public event EventHandler<DetectedGameChangedEventArgs>? DetectedGameChanged;

        internal bool UsesPollingProcessDetection
        {
            get
            {
                lock (_syncRoot)
                {
                    return _usesPollingProcessDetection;
                }
            }
        }

        private DetectedGame? CurrentGame
        {
            get
            {
                lock (_syncRoot)
                {
                    return _currentGame;
                }
            }
        }

        private bool IsConnecting
        {
            get
            {
                lock (_syncRoot)
                {
                    return _lifecycle.IsConnecting;
                }
            }
        }

        private bool IsDisconnecting
        {
            get
            {
                lock (_syncRoot)
                {
                    return _lifecycle.IsDisconnecting;
                }
            }
        }

        public void Start()
        {
            try
            {
                _processDetectionService.Start();
            }
            catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
            {
                lock (_syncRoot)
                {
                    _usesPollingProcessDetection = true;
                }
            }

            ApplyDetectedGame(_processDetectionService.CurrentGame, notify: false);
        }

        public GameConnectionRefreshResult Read()
        {
            long diagnosticsStartedAt = RefreshDiagnostics.Start();
            try
            {
                DateTimeOffset receivedAt = _timeProvider.GetUtcNow();
                DetectedGame? detectedGame = RefreshCurrentGame();
                if (IsDisconnecting)
                {
                    return ReadDisconnectSnapshot(detectedGame, receivedAt);
                }

                return ReadSnapshot(detectedGame, receivedAt);
            }
            finally
            {
                RefreshDiagnostics.WriteElapsed("game connection refresh", diagnosticsStartedAt);
            }
        }

        public GameConnectionRefreshResult GetStatusSnapshot()
        {
            RefreshCurrentGame();
            lock (_syncRoot)
            {
                return CreateStatusSnapshotLocked(_currentGame);
            }
        }

        public GameConnectionRefreshResult HandleReadFailure()
        {
            RefreshCurrentGame();
            lock (_syncRoot)
            {
                _lifecycle.ClearTransientOperationState();
                return CreateStatusSnapshotLocked(_currentGame);
            }
        }

        internal bool IsMonitorConnectedFor(DetectedGame? detectedGame)
        {
            lock (_syncRoot)
            {
                return _lifecycle.IsMonitorConnectedFor(
                    GameConnectionSessionLifecycleGame.FromDetectedGame(detectedGame));
            }
        }

        public GameConnectionRefreshResult Connect()
        {
            GameConnectionRefreshResult connectingSnapshot = BeginConnect();
            if (!connectingSnapshot.IsConnecting)
            {
                return connectingSnapshot;
            }

            return CompleteConnect();
        }

        public GameConnectionRefreshResult BeginConnect()
        {
            DetectedGame? detectedGame = RefreshCurrentGame();
            lock (_syncRoot)
            {
                _lifecycle.BeginConnect(
                    GameConnectionSessionLifecycleGame.FromDetectedGame(detectedGame));
                return CreateStatusSnapshotLocked(detectedGame);
            }
        }

        public GameConnectionRefreshResult CompleteConnect()
        {
            GameConnectionSessionLifecycleGame? connectTargetGame = GetConnectTargetGame();
            try
            {
                return CompleteConnect(Inject());
            }
            catch
            {
                RollbackFailedConnect(connectTargetGame);
                throw;
            }
        }

        public void CancelConnect()
        {
            lock (_syncRoot)
            {
                _lifecycle.CancelConnect();
            }
        }

        private void RollbackFailedConnect(GameConnectionSessionLifecycleGame? connectTargetGame)
        {
            GameConnectionSessionMonitorStopRequest stopRequest;
            lock (_syncRoot)
            {
                stopRequest = _lifecycle.RollbackFailedConnect(connectTargetGame);
            }

            if (stopRequest.ShouldRequestStop)
            {
                _eventMonitor.RequestStop(stopRequest.MonitorProcessId);
            }
        }

        private GameConnectionSessionLifecycleGame? GetConnectTargetGame()
        {
            lock (_syncRoot)
            {
                return _lifecycle.ConnectTargetGame;
            }
        }

        private DllInjectionResult Inject()
        {
            DetectedGame? detectedGame = RefreshCurrentGame();
            lock (_syncRoot)
            {
                if (!_lifecycle.CanCompleteConnectFor(
                    GameConnectionSessionLifecycleGame.FromDetectedGame(detectedGame)))
                {
                    return DllInjectionResult.NotAttempted;
                }
            }

            return _dllInjector.Inject(detectedGame!);
        }

        private GameConnectionRefreshResult CompleteConnect(DllInjectionResult injectionResult)
        {
            ArgumentNullException.ThrowIfNull(injectionResult);

            DateTimeOffset receivedAt = _timeProvider.GetUtcNow();
            DetectedGame? detectedGame = RefreshCurrentGame();
            lock (_syncRoot)
            {
                _lifecycle.CompleteConnect(
                    GameConnectionSessionLifecycleGame.FromDetectedGame(detectedGame),
                    injectionResult,
                    receivedAt);
            }

            return ReadSnapshot(detectedGame, receivedAt);
        }

        public GameConnectionRefreshResult BeginDisconnect()
        {
            DateTimeOffset receivedAt = _timeProvider.GetUtcNow();
            DetectedGame? detectedGame = RefreshCurrentGame();
            GameConnectionSessionDisconnectAction disconnectAction;
            GameConnectionRefreshResult? result = null;
            lock (_syncRoot)
            {
                disconnectAction = _lifecycle.BeginDisconnect(receivedAt);
                if (!disconnectAction.ShouldReadSnapshot)
                {
                    result = CreateDisconnectingSnapshotLocked(detectedGame);
                }
            }

            if (disconnectAction.ShouldRequestStop)
            {
                _eventMonitor.RequestStop(disconnectAction.MonitorProcessId);
            }

            return disconnectAction.ShouldReadSnapshot
                ? ReadSnapshot(detectedGame, receivedAt)
                : result!.Value;
        }

        public void ResetMonitorConnectionState(bool requestStop = true)
        {
            GameConnectionSessionMonitorStopRequest stopRequest;
            lock (_syncRoot)
            {
                stopRequest = _lifecycle.ResetMonitorConnectionState(requestStop);
            }

            if (stopRequest.ShouldRequestStop)
            {
                _eventMonitor.RequestStop(stopRequest.MonitorProcessId);
            }
        }

        public void Dispose()
        {
            _processDetectionService.DetectedGameChanged -= OnDetectedGameChanged;
            _processDetectionService.Dispose();
            _eventMonitor.Dispose();
            _memoryReader.Dispose();
        }

        private GameConnectionRefreshResult ReadDisconnectSnapshot(
            DetectedGame? detectedGame,
            DateTimeOffset receivedAt)
        {
            int? disconnectProcessId;
            DateTimeOffset? disconnectRequestedAt;
            bool isDisconnecting;
            lock (_syncRoot)
            {
                isDisconnecting = _lifecycle.IsDisconnecting;
                disconnectProcessId = _lifecycle.DisconnectProcessId;
                disconnectRequestedAt = _lifecycle.DisconnectRequestedAt;
            }

            if (!isDisconnecting)
            {
                return ReadSnapshot(detectedGame, receivedAt);
            }

            bool isComplete = disconnectProcessId is not int processId
                || _eventMonitor.IsStopComplete(processId)
                || (disconnectRequestedAt is DateTimeOffset requestedAt
                    && receivedAt - requestedAt >= MonitorDisconnectTimeout);
            if (!isComplete)
            {
                lock (_syncRoot)
                {
                    if (_lifecycle.IsDisconnecting && _lifecycle.DisconnectProcessId == disconnectProcessId)
                    {
                        return CreateDisconnectingSnapshotLocked(detectedGame);
                    }
                }

                return ReadSnapshot(detectedGame, receivedAt);
            }

            lock (_syncRoot)
            {
                if (_lifecycle.IsDisconnecting && _lifecycle.DisconnectProcessId == disconnectProcessId)
                {
                    _lifecycle.ResetMonitorConnectionState(requestStop: false);
                }
            }

            return ReadSnapshot(detectedGame, receivedAt);
        }

        private GameConnectionSessionMonitorStopRequest ApplyMonitorReadinessTimeoutLocked(
            DetectedGame? detectedGame,
            GameEventMonitorStatus eventStatus,
            DateTimeOffset now)
        {
            return _lifecycle.ApplyMonitorReadinessTimeout(
                GameConnectionSessionLifecycleGame.FromDetectedGame(detectedGame),
                eventStatus,
                now,
                MonitorReadinessRetryTimeout,
                AppStrings.Get("DllInjectionReadinessTimedOut"));
        }

        private void OnDetectedGameChanged(object? sender, DetectedGameChangedEventArgs args)
        {
            ApplyDetectedGame(args.DetectedGame, notify: true);
        }

        private DetectedGame? RefreshCurrentGame()
        {
            if (UsesPollingProcessDetection)
            {
                ApplyDetectedGame(DetectByPolling(), notify: false);
            }

            return CurrentGame;
        }

        private DetectedGame? DetectByPolling()
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

        private GameConnectionRefreshResult ReadSnapshot(
            DetectedGame? detectedGame,
            DateTimeOffset receivedAt)
        {
            PlayerStatsReadResult readResult = _memoryReader.ReadPlayerStats(detectedGame);
            int? ownedMonitorProcessId;
            lock (_syncRoot)
            {
                if (!Equals(_currentGame, detectedGame))
                {
                    return CreateStatusSnapshotLocked(_currentGame);
                }

                ownedMonitorProcessId = detectedGame is not null
                    && _lifecycle.IsMonitorConnectedFor(
                        GameConnectionSessionLifecycleGame.FromDetectedGame(detectedGame))
                    && readResult.DetectedGame?.ProcessId == detectedGame.ProcessId
                        ? detectedGame.ProcessId
                        : null;
            }

            GameEventMonitorStatus eventStatus = ownedMonitorProcessId is int processId
                ? _eventMonitor.ReadStatus(receivedAt, processId)
                : GameEventMonitorStatus.WaitingForMonitor;

            GameConnectionRefreshResult result;
            GameConnectionSessionMonitorStopRequest stopRequest;
            lock (_syncRoot)
            {
                if (!Equals(_currentGame, detectedGame))
                {
                    return CreateStatusSnapshotLocked(_currentGame);
                }

                stopRequest = ApplyMonitorReadinessTimeoutLocked(readResult.DetectedGame, eventStatus, receivedAt);
                result = CreateRefreshResultLocked(detectedGame, readResult, eventStatus);
            }

            if (stopRequest.ShouldRequestStop)
            {
                _eventMonitor.RequestStop(stopRequest.MonitorProcessId);
            }

            return result;
        }

        private PlayerStatsReadResult CreateStatusReadResultLocked(DetectedGame? detectedGame)
        {
            if (detectedGame is null)
            {
                return PlayerStatsReadResult.GameNotRunning;
            }

            if (detectedGame.AddressMap is null)
            {
                string statusText = string.IsNullOrWhiteSpace(detectedGame.UnsupportedReason)
                    ? AppStrings.Format("UnsupportedStatusFormat", detectedGame.DisplayName)
                    : AppStrings.Format("UnsupportedStatusWithReasonFormat", detectedGame.DisplayName, detectedGame.UnsupportedReason);

                return new PlayerStatsReadResult(
                    detectedGame,
                    null,
                    statusText,
                    ConnectionState.Unsupported);
            }

            if (_lifecycle.IsDisconnecting
                && _lifecycle.IsMonitorConnectedFor(
                    GameConnectionSessionLifecycleGame.FromDetectedGame(detectedGame)))
            {
                return new PlayerStatsReadResult(
                    detectedGame,
                    null,
                    AppStrings.Get("ConnectionStatusDisconnecting"),
                    ConnectionState.Disconnecting);
            }

            if (_lifecycle.IsMonitorConnectedFor(
                GameConnectionSessionLifecycleGame.FromDetectedGame(detectedGame)))
            {
                return new PlayerStatsReadResult(
                    detectedGame,
                    null,
                    AppStrings.Format("ConnectedStatusFormat", detectedGame.DisplayName),
                    ConnectionState.Connected);
            }

            return new PlayerStatsReadResult(
                detectedGame,
                null,
                AppStrings.Format("GameDetectedConnectPromptFormat", detectedGame.DisplayName),
                ConnectionState.Detected);
        }

        private void ApplyDetectedGame(DetectedGame? detectedGame, bool notify)
        {
            EventHandler<DetectedGameChangedEventArgs>? handler;
            GameConnectionSessionMonitorStopRequest stopRequest;
            lock (_syncRoot)
            {
                if (Equals(_currentGame, detectedGame))
                {
                    return;
                }

                _currentGame = detectedGame;
                handler = DetectedGameChanged;
                stopRequest = _lifecycle.ResetMonitorConnectionState();
            }

            if (stopRequest.ShouldRequestStop)
            {
                _eventMonitor.RequestStop(stopRequest.MonitorProcessId);
            }

            if (notify)
            {
                handler?.Invoke(this, new DetectedGameChangedEventArgs(detectedGame));
            }
        }

        private GameConnectionRefreshResult CreateStatusSnapshotLocked(DetectedGame? detectedGame)
        {
            return CreateRefreshResultLocked(
                detectedGame,
                CreateStatusReadResultLocked(detectedGame),
                GameEventMonitorStatus.WaitingForMonitor);
        }

        private GameConnectionRefreshResult CreateDisconnectingSnapshotLocked(DetectedGame? detectedGame)
        {
            return CreateRefreshResultLocked(
                detectedGame,
                CreateStatusReadResultLocked(detectedGame),
                GameEventMonitorStatus.WaitingForMonitor);
        }

        private GameConnectionRefreshResult CreateRefreshResultLocked(
            DetectedGame? detectedGame,
            PlayerStatsReadResult readResult,
            GameEventMonitorStatus eventStatus)
        {
            GameConnectionSessionLifecycleSnapshot lifecycleSnapshot = _lifecycle.CreateSnapshot(
                GameConnectionSessionLifecycleGame.FromDetectedGame(detectedGame));
            return new GameConnectionRefreshResult(
                detectedGame,
                readResult,
                eventStatus,
                lifecycleSnapshot.InjectionResult,
                lifecycleSnapshot.IsConnecting,
                lifecycleSnapshot.IsDisconnecting,
                lifecycleSnapshot.CanAttemptConnect,
                lifecycleSnapshot.HasInjectionAttemptForCurrentGame,
                lifecycleSnapshot.IsMonitorConnectedForCurrentGame);
        }
    }

    internal readonly record struct GameConnectionRefreshResult(
        DetectedGame? CurrentGame,
        PlayerStatsReadResult ReadResult,
        GameEventMonitorStatus EventStatus,
        DllInjectionResult InjectionResult,
        bool IsConnecting,
        bool IsDisconnecting,
        bool CanAttemptConnect,
        bool HasInjectionAttemptForCurrentGame,
        bool IsMonitorConnectedForCurrentGame);
}
