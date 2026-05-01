using System;

namespace BO2.Services
{
    internal sealed class GameConnectionSession : IDisposable
    {
        private static readonly TimeSpan MonitorReadinessRetryTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan MonitorDisconnectTimeout = TimeSpan.FromSeconds(3);

        private readonly object _syncRoot = new();
        private readonly GameSessionCoordinator _gameSession;
        private readonly TimeProvider _timeProvider;
        private DetectedGame? _currentGame;
        private DllInjectionResult _lastInjectionResult = DllInjectionResult.NotAttempted;
        private int? _lastInjectionProcessId;
        private DateTimeOffset? _lastInjectionAttemptedAt;
        private DetectedGame? _connectTargetGame;
        private int? _disconnectProcessId;
        private DateTimeOffset? _disconnectRequestedAt;

        public GameConnectionSession()
            : this(new GameSessionCoordinator(), TimeProvider.System)
        {
        }

        internal GameConnectionSession(GameSessionCoordinator gameSession)
            : this(gameSession, TimeProvider.System)
        {
        }

        internal GameConnectionSession(GameSessionCoordinator gameSession, TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(gameSession);
            ArgumentNullException.ThrowIfNull(timeProvider);

            _gameSession = gameSession;
            _timeProvider = timeProvider;
            _currentGame = _gameSession.CurrentGame;
            _gameSession.DetectedGameChanged += OnDetectedGameChanged;
        }

        public event EventHandler<DetectedGameChangedEventArgs>? DetectedGameChanged;

        public bool UsesPollingProcessDetection => _gameSession.UsesPollingProcessDetection;

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

        public bool IsConnecting { get; private set; }

        public bool IsDisconnecting { get; private set; }

        public DllInjectionResult LastInjectionResult => _lastInjectionResult;

        public void Start()
        {
            _gameSession.Start();
            ApplyDetectedGame(_gameSession.CurrentGame, notify: false);
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

        public bool CanAttemptConnect(DetectedGame? detectedGame)
        {
            return !IsConnecting
                && !IsDisconnecting
                && detectedGame is not null
                && detectedGame.Variant == GameVariant.SteamZombies
                && detectedGame.IsStatsSupported
                && !IsMonitorConnectedFor(detectedGame);
        }

        public bool HasInjectionAttemptFor(DetectedGame? detectedGame)
        {
            return detectedGame is not null
                && _lastInjectionProcessId == detectedGame.ProcessId
                && _lastInjectionResult.State != DllInjectionState.NotAttempted;
        }

        public bool IsMonitorConnectedFor(DetectedGame? detectedGame)
        {
            return detectedGame is not null
                && _lastInjectionProcessId == detectedGame.ProcessId
                && IsMonitorLoadedInjectionState(_lastInjectionResult.State);
        }

        public GameConnectionRefreshResult BeginConnect()
        {
            DetectedGame? detectedGame = RefreshCurrentGame();
            if (IsConnecting)
            {
                return CreateStatusSnapshot(detectedGame);
            }

            if (!CanAttemptConnect(detectedGame))
            {
                _connectTargetGame = null;
                return CreateStatusSnapshot(detectedGame);
            }

            _connectTargetGame = detectedGame;
            IsConnecting = true;
            return CreateStatusSnapshot(detectedGame);
        }

        public void CancelConnect()
        {
            _connectTargetGame = null;
            IsConnecting = false;
        }

        public void ClearTransientOperationState()
        {
            IsConnecting = false;
            IsDisconnecting = false;
            _connectTargetGame = null;
            _disconnectProcessId = null;
            _disconnectRequestedAt = null;
        }

        public DllInjectionResult Inject()
        {
            DetectedGame? connectTargetGame = _connectTargetGame;
            DetectedGame? detectedGame = RefreshCurrentGame();
            if (!IsConnecting || connectTargetGame is null || !Equals(detectedGame, connectTargetGame))
            {
                return DllInjectionResult.NotAttempted;
            }

            return _gameSession.Inject(connectTargetGame);
        }

        public GameConnectionRefreshResult CompleteConnect(DllInjectionResult injectionResult)
        {
            ArgumentNullException.ThrowIfNull(injectionResult);

            DateTimeOffset receivedAt = _timeProvider.GetUtcNow();
            DetectedGame? connectTargetGame = _connectTargetGame;
            DetectedGame? detectedGame = RefreshCurrentGame();
            if (IsConnecting && connectTargetGame is not null && Equals(detectedGame, connectTargetGame))
            {
                _lastInjectionProcessId = connectTargetGame.ProcessId;
                _lastInjectionResult = injectionResult;
                _lastInjectionAttemptedAt = IsMonitorLoadedInjectionState(injectionResult.State)
                    ? receivedAt
                    : null;
            }

            _connectTargetGame = null;
            IsConnecting = false;

            return ReadSnapshot(detectedGame, receivedAt);
        }

        public GameConnectionRefreshResult BeginDisconnect()
        {
            DateTimeOffset receivedAt = _timeProvider.GetUtcNow();
            DetectedGame? detectedGame = RefreshCurrentGame();
            if (IsDisconnecting)
            {
                return CreateDisconnectingSnapshot(detectedGame);
            }

            int? monitorProcessId = _lastInjectionProcessId;
            if (monitorProcessId is null || !IsMonitorLoadedInjectionState(_lastInjectionResult.State))
            {
                ResetMonitorConnectionState(requestStop: false);
                return ReadSnapshot(detectedGame, receivedAt);
            }

            IsConnecting = false;
            _connectTargetGame = null;
            IsDisconnecting = true;
            _disconnectProcessId = monitorProcessId;
            _disconnectRequestedAt = receivedAt;
            _gameSession.RequestMonitorStop(monitorProcessId);
            return CreateDisconnectingSnapshot(detectedGame);
        }

        public void ResetMonitorConnectionState(bool requestStop = true)
        {
            int? monitorProcessId = _lastInjectionProcessId;
            bool stopAlreadyRequested = IsDisconnecting
                && monitorProcessId is not null
                && _disconnectProcessId == monitorProcessId;
            IsConnecting = false;
            IsDisconnecting = false;
            _connectTargetGame = null;
            _disconnectProcessId = null;
            _disconnectRequestedAt = null;
            _lastInjectionProcessId = null;
            _lastInjectionAttemptedAt = null;
            _lastInjectionResult = DllInjectionResult.NotAttempted;
            if (requestStop && !stopAlreadyRequested)
            {
                _gameSession.RequestMonitorStop(monitorProcessId);
            }
        }

        public void Dispose()
        {
            _gameSession.DetectedGameChanged -= OnDetectedGameChanged;
            _gameSession.Dispose();
        }

        private static bool IsMonitorLoadedInjectionState(DllInjectionState state)
        {
            return state is DllInjectionState.Loaded or DllInjectionState.AlreadyInjected;
        }

        private GameConnectionRefreshResult ReadDisconnectSnapshot(
            DetectedGame? detectedGame,
            DateTimeOffset receivedAt)
        {
            if (!IsMonitorDisconnectComplete(receivedAt))
            {
                return CreateDisconnectingSnapshot(detectedGame);
            }

            CompleteDisconnect();
            return ReadSnapshot(detectedGame, receivedAt);
        }

        private bool IsMonitorDisconnectComplete(DateTimeOffset now)
        {
            if (!IsDisconnecting)
            {
                return true;
            }

            if (_disconnectProcessId is not int processId)
            {
                return true;
            }

            if (_gameSession.IsMonitorStopComplete(processId))
            {
                return true;
            }

            return _disconnectRequestedAt is DateTimeOffset requestedAt
                && now - requestedAt >= MonitorDisconnectTimeout;
        }

        private void CompleteDisconnect()
        {
            ResetMonitorConnectionState(requestStop: false);
        }

        private void ApplyMonitorReadinessTimeout(
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
                return;
            }

            _gameSession.RequestMonitorStop(_lastInjectionProcessId);
            _lastInjectionResult = new DllInjectionResult(
                DllInjectionState.Failed,
                AppStrings.Get("DllInjectionReadinessTimedOut"));
            _lastInjectionAttemptedAt = null;
        }

        private void OnDetectedGameChanged(object? sender, DetectedGameChangedEventArgs args)
        {
            ApplyDetectedGame(args.DetectedGame, notify: true);
        }

        private DetectedGame? RefreshCurrentGame()
        {
            if (UsesPollingProcessDetection)
            {
                ApplyDetectedGame(_gameSession.DetectByPolling(), notify: false);
            }

            return CurrentGame;
        }

        private GameConnectionRefreshResult ReadSnapshot(
            DetectedGame? detectedGame,
            DateTimeOffset receivedAt)
        {
            int? ownedMonitorProcessId = IsMonitorConnectedFor(detectedGame)
                ? detectedGame?.ProcessId
                : null;
            PlayerStatsReadResult readResult = _gameSession.ReadPlayerStats(detectedGame);
            GameEventMonitorStatus eventStatus = ownedMonitorProcessId is int processId
                && readResult.DetectedGame?.ProcessId == processId
                ? _gameSession.ReadEventMonitorStatus(receivedAt, processId)
                : GameEventMonitorStatus.WaitingForMonitor;

            ApplyMonitorReadinessTimeout(readResult.DetectedGame, eventStatus, receivedAt);
            return CreateRefreshResult(detectedGame, readResult, eventStatus);
        }

        private GameConnectionRefreshResult CreateStatusSnapshot(DetectedGame? detectedGame)
        {
            return CreateRefreshResult(
                detectedGame,
                CreateStatusReadResult(detectedGame),
                GameEventMonitorStatus.WaitingForMonitor);
        }

        private GameConnectionRefreshResult CreateDisconnectingSnapshot(DetectedGame? detectedGame)
        {
            return CreateRefreshResult(
                detectedGame,
                CreateStatusReadResult(detectedGame),
                GameEventMonitorStatus.WaitingForMonitor);
        }

        private static PlayerStatsReadResult CreateStatusReadResult(DetectedGame? detectedGame)
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

            return new PlayerStatsReadResult(
                detectedGame,
                null,
                AppStrings.Format("GameDetectedConnectPromptFormat", detectedGame.DisplayName),
                ConnectionState.Detected);
        }

        private void ApplyDetectedGame(DetectedGame? detectedGame, bool notify)
        {
            EventHandler<DetectedGameChangedEventArgs>? handler;
            lock (_syncRoot)
            {
                if (Equals(_currentGame, detectedGame))
                {
                    return;
                }

                _currentGame = detectedGame;
                handler = DetectedGameChanged;
            }

            ResetMonitorConnectionState();
            if (notify)
            {
                handler?.Invoke(this, new DetectedGameChangedEventArgs(detectedGame));
            }
        }

        private GameConnectionRefreshResult CreateRefreshResult(
            DetectedGame? detectedGame,
            PlayerStatsReadResult readResult,
            GameEventMonitorStatus eventStatus)
        {
            return new GameConnectionRefreshResult(
                detectedGame,
                readResult,
                eventStatus,
                _lastInjectionResult,
                IsConnecting,
                IsDisconnecting,
                CanAttemptConnect(detectedGame),
                HasInjectionAttemptFor(detectedGame),
                IsMonitorConnectedFor(detectedGame));
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
