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
        private DetectedGame? _currentGame;
        private bool _usesPollingProcessDetection;
        private bool _isConnecting;
        private bool _isDisconnecting;
        private DllInjectionResult _lastInjectionResult = DllInjectionResult.NotAttempted;
        private int? _lastInjectionProcessId;
        private DateTimeOffset? _lastInjectionAttemptedAt;
        private DetectedGame? _connectTargetGame;
        private int? _disconnectProcessId;
        private DateTimeOffset? _disconnectRequestedAt;

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
                    return _isConnecting;
                }
            }
        }

        private bool IsDisconnecting
        {
            get
            {
                lock (_syncRoot)
                {
                    return _isDisconnecting;
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
                ClearTransientOperationStateLocked();
                return CreateStatusSnapshotLocked(_currentGame);
            }
        }

        internal bool IsMonitorConnectedFor(DetectedGame? detectedGame)
        {
            lock (_syncRoot)
            {
                return IsMonitorConnectedForLocked(detectedGame);
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
                if (_isConnecting)
                {
                    return CreateStatusSnapshotLocked(detectedGame);
                }

                if (!CanAttemptConnectLocked(detectedGame))
                {
                    _connectTargetGame = null;
                    return CreateStatusSnapshotLocked(detectedGame);
                }

                _connectTargetGame = detectedGame;
                _isConnecting = true;
                return CreateStatusSnapshotLocked(detectedGame);
            }
        }

        public GameConnectionRefreshResult CompleteConnect()
        {
            DetectedGame? connectTargetGame = GetConnectTargetGame();
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
                if (!_isConnecting)
                {
                    return;
                }

                _connectTargetGame = null;
                _isConnecting = false;
            }
        }

        private void RollbackFailedConnect(DetectedGame? connectTargetGame)
        {
            (int? monitorProcessId, bool shouldRequestStop) stopRequest = (null, false);
            lock (_syncRoot)
            {
                if (_isConnecting)
                {
                    _connectTargetGame = null;
                    _isConnecting = false;
                    return;
                }

                if (connectTargetGame is not null && _lastInjectionProcessId == connectTargetGame.ProcessId)
                {
                    stopRequest = ResetMonitorConnectionStateLocked(
                        IsMonitorLoadedInjectionState(_lastInjectionResult.State));
                }
            }

            if (stopRequest.shouldRequestStop)
            {
                _eventMonitor.RequestStop(stopRequest.monitorProcessId);
            }
        }

        private DetectedGame? GetConnectTargetGame()
        {
            lock (_syncRoot)
            {
                return _connectTargetGame;
            }
        }

        private DllInjectionResult Inject()
        {
            DetectedGame? detectedGame = RefreshCurrentGame();
            DetectedGame? connectTargetGame;
            lock (_syncRoot)
            {
                connectTargetGame = _connectTargetGame;
                if (!_isConnecting || connectTargetGame is null || !Equals(detectedGame, connectTargetGame))
                {
                    return DllInjectionResult.NotAttempted;
                }
            }

            return _dllInjector.Inject(connectTargetGame);
        }

        private GameConnectionRefreshResult CompleteConnect(DllInjectionResult injectionResult)
        {
            ArgumentNullException.ThrowIfNull(injectionResult);

            DateTimeOffset receivedAt = _timeProvider.GetUtcNow();
            DetectedGame? detectedGame = RefreshCurrentGame();
            lock (_syncRoot)
            {
                DetectedGame? connectTargetGame = _connectTargetGame;
                if (_isConnecting && connectTargetGame is not null && Equals(detectedGame, connectTargetGame))
                {
                    _lastInjectionProcessId = connectTargetGame.ProcessId;
                    _lastInjectionResult = injectionResult;
                    _lastInjectionAttemptedAt = IsMonitorLoadedInjectionState(injectionResult.State)
                        ? receivedAt
                        : null;
                }

                _connectTargetGame = null;
                _isConnecting = false;
            }

            return ReadSnapshot(detectedGame, receivedAt);
        }

        public GameConnectionRefreshResult BeginDisconnect()
        {
            DateTimeOffset receivedAt = _timeProvider.GetUtcNow();
            DetectedGame? detectedGame = RefreshCurrentGame();
            int? stopProcessId = null;
            GameConnectionRefreshResult? result = null;
            bool readSnapshot = false;
            lock (_syncRoot)
            {
                if (_isDisconnecting)
                {
                    return CreateDisconnectingSnapshotLocked(detectedGame);
                }

                int? monitorProcessId = _lastInjectionProcessId;
                if (monitorProcessId is null || !IsMonitorLoadedInjectionState(_lastInjectionResult.State))
                {
                    ResetMonitorConnectionStateLocked(requestStop: false);
                    readSnapshot = true;
                }
                else
                {
                    _isConnecting = false;
                    _connectTargetGame = null;
                    _isDisconnecting = true;
                    _disconnectProcessId = monitorProcessId;
                    _disconnectRequestedAt = receivedAt;
                    stopProcessId = monitorProcessId;
                    result = CreateDisconnectingSnapshotLocked(detectedGame);
                }
            }

            if (stopProcessId is not null)
            {
                _eventMonitor.RequestStop(stopProcessId);
            }

            return readSnapshot
                ? ReadSnapshot(detectedGame, receivedAt)
                : result!.Value;
        }

        public void ResetMonitorConnectionState(bool requestStop = true)
        {
            (int? monitorProcessId, bool shouldRequestStop) stopRequest;
            lock (_syncRoot)
            {
                stopRequest = ResetMonitorConnectionStateLocked(requestStop);
            }

            if (stopRequest.shouldRequestStop)
            {
                _eventMonitor.RequestStop(stopRequest.monitorProcessId);
            }
        }

        public void Dispose()
        {
            _processDetectionService.DetectedGameChanged -= OnDetectedGameChanged;
            _processDetectionService.Dispose();
            _eventMonitor.Dispose();
            _memoryReader.Dispose();
        }

        private static bool IsMonitorLoadedInjectionState(DllInjectionState state)
        {
            return state is DllInjectionState.Loaded or DllInjectionState.AlreadyInjected;
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
                isDisconnecting = _isDisconnecting;
                disconnectProcessId = _disconnectProcessId;
                disconnectRequestedAt = _disconnectRequestedAt;
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
                    if (_isDisconnecting && _disconnectProcessId == disconnectProcessId)
                    {
                        return CreateDisconnectingSnapshotLocked(detectedGame);
                    }
                }

                return ReadSnapshot(detectedGame, receivedAt);
            }

            lock (_syncRoot)
            {
                if (_isDisconnecting && _disconnectProcessId == disconnectProcessId)
                {
                    ResetMonitorConnectionStateLocked(requestStop: false);
                }
            }

            return ReadSnapshot(detectedGame, receivedAt);
        }

        private (int? monitorProcessId, bool shouldRequestStop) ApplyMonitorReadinessTimeoutLocked(
            DetectedGame? detectedGame,
            GameEventMonitorStatus eventStatus,
            DateTimeOffset now)
        {
            if (detectedGame is null
                || _lastInjectionProcessId != detectedGame.ProcessId
                || eventStatus.CompatibilityState != GameCompatibilityState.WaitingForMonitor
                || !IsMonitorLoadedInjectionState(_lastInjectionResult.State)
                || _lastInjectionAttemptedAt is not DateTimeOffset attemptedAt
                || now - attemptedAt < MonitorReadinessRetryTimeout)
            {
                return (null, false);
            }

            int? monitorProcessId = _lastInjectionProcessId;
            _lastInjectionResult = new DllInjectionResult(
                DllInjectionState.Failed,
                AppStrings.Get("DllInjectionReadinessTimedOut"));
            _lastInjectionAttemptedAt = null;
            return (monitorProcessId, true);
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
                    && IsMonitorConnectedForLocked(detectedGame)
                    && readResult.DetectedGame?.ProcessId == detectedGame.ProcessId
                        ? detectedGame.ProcessId
                        : null;
            }

            GameEventMonitorStatus eventStatus = ownedMonitorProcessId is int processId
                ? _eventMonitor.ReadStatus(receivedAt, processId)
                : GameEventMonitorStatus.WaitingForMonitor;

            GameConnectionRefreshResult result;
            (int? monitorProcessId, bool shouldRequestStop) stopRequest;
            lock (_syncRoot)
            {
                if (!Equals(_currentGame, detectedGame))
                {
                    return CreateStatusSnapshotLocked(_currentGame);
                }

                stopRequest = ApplyMonitorReadinessTimeoutLocked(readResult.DetectedGame, eventStatus, receivedAt);
                result = CreateRefreshResultLocked(detectedGame, readResult, eventStatus);
            }

            if (stopRequest.shouldRequestStop)
            {
                _eventMonitor.RequestStop(stopRequest.monitorProcessId);
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

            if (_isDisconnecting && IsMonitorConnectedForLocked(detectedGame))
            {
                return new PlayerStatsReadResult(
                    detectedGame,
                    null,
                    AppStrings.Get("ConnectionStatusDisconnecting"),
                    ConnectionState.Disconnecting);
            }

            if (IsMonitorConnectedForLocked(detectedGame))
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
            (int? monitorProcessId, bool shouldRequestStop) stopRequest;
            lock (_syncRoot)
            {
                if (Equals(_currentGame, detectedGame))
                {
                    return;
                }

                _currentGame = detectedGame;
                handler = DetectedGameChanged;
                stopRequest = ResetMonitorConnectionStateLocked();
            }

            if (stopRequest.shouldRequestStop)
            {
                _eventMonitor.RequestStop(stopRequest.monitorProcessId);
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
            return new GameConnectionRefreshResult(
                detectedGame,
                readResult,
                eventStatus,
                _lastInjectionResult,
                _isConnecting,
                _isDisconnecting,
                CanAttemptConnectLocked(detectedGame),
                HasInjectionAttemptForLocked(detectedGame),
                IsMonitorConnectedForLocked(detectedGame));
        }

        private bool CanAttemptConnectLocked(DetectedGame? detectedGame)
        {
            return !_isConnecting
                && !_isDisconnecting
                && detectedGame is not null
                && detectedGame.Variant == GameVariant.SteamZombies
                && detectedGame.IsStatsSupported
                && !IsMonitorConnectedForLocked(detectedGame);
        }

        private bool HasInjectionAttemptForLocked(DetectedGame? detectedGame)
        {
            return detectedGame is not null
                && _lastInjectionProcessId == detectedGame.ProcessId
                && _lastInjectionResult.State != DllInjectionState.NotAttempted;
        }

        private bool IsMonitorConnectedForLocked(DetectedGame? detectedGame)
        {
            return detectedGame is not null
                && _lastInjectionProcessId == detectedGame.ProcessId
                && IsMonitorLoadedInjectionState(_lastInjectionResult.State);
        }

        private void ClearTransientOperationStateLocked()
        {
            _isConnecting = false;
            _isDisconnecting = false;
            _connectTargetGame = null;
            _disconnectProcessId = null;
            _disconnectRequestedAt = null;
        }

        private (int? monitorProcessId, bool shouldRequestStop) ResetMonitorConnectionStateLocked(bool requestStop = true)
        {
            int? monitorProcessId = _lastInjectionProcessId;
            bool stopAlreadyRequested = _isDisconnecting
                && monitorProcessId is not null
                && _disconnectProcessId == monitorProcessId;
            ClearTransientOperationStateLocked();
            _lastInjectionProcessId = null;
            _lastInjectionAttemptedAt = null;
            _lastInjectionResult = DllInjectionResult.NotAttempted;
            return (monitorProcessId, requestStop && !stopAlreadyRequested);
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
