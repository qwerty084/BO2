using System;

namespace BO2.Services
{
    internal sealed class GameConnectionSession : IDisposable
    {
        private static readonly TimeSpan MonitorReadinessRetryTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan MonitorDisconnectTimeout = TimeSpan.FromSeconds(3);

        private readonly GameSessionCoordinator _gameSession;
        private readonly TimeProvider _timeProvider;
        private DllInjectionResult _lastInjectionResult = DllInjectionResult.NotAttempted;
        private int? _lastInjectionProcessId;
        private DateTimeOffset? _lastInjectionAttemptedAt;
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
        }

        public event EventHandler<DetectedGameChangedEventArgs>? DetectedGameChanged
        {
            add => _gameSession.DetectedGameChanged += value;
            remove => _gameSession.DetectedGameChanged -= value;
        }

        public bool UsesPollingProcessDetection => _gameSession.UsesPollingProcessDetection;

        public DetectedGame? CurrentGame => _gameSession.CurrentGame;

        public bool IsConnecting { get; private set; }

        public bool IsDisconnecting { get; private set; }

        public DllInjectionResult LastInjectionResult => _lastInjectionResult;

        public void Start()
        {
            _gameSession.Start();
        }

        public DetectedGame? DetectByPolling()
        {
            return _gameSession.DetectByPolling();
        }

        public GameConnectionRefreshResult Read(DetectedGame? detectedGame)
        {
            long diagnosticsStartedAt = RefreshDiagnostics.Start();
            try
            {
                DateTimeOffset receivedAt = _timeProvider.GetUtcNow();
                int? ownedMonitorProcessId = IsMonitorConnectedFor(detectedGame)
                    ? detectedGame?.ProcessId
                    : null;
                PlayerStatsReadResult readResult = _gameSession.ReadPlayerStats(detectedGame);
                GameEventMonitorStatus eventStatus = ownedMonitorProcessId is int processId
                    && readResult.DetectedGame?.ProcessId == processId
                    ? _gameSession.ReadEventMonitorStatus(receivedAt, processId)
                    : GameEventMonitorStatus.WaitingForMonitor;

                ApplyMonitorReadinessTimeout(readResult.DetectedGame, eventStatus, receivedAt);
                return new GameConnectionRefreshResult(readResult, eventStatus, _lastInjectionResult);
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

        public void BeginConnect()
        {
            IsConnecting = true;
        }

        public void CancelConnect()
        {
            IsConnecting = false;
        }

        public void ClearTransientOperationState()
        {
            IsConnecting = false;
            IsDisconnecting = false;
            _disconnectProcessId = null;
            _disconnectRequestedAt = null;
        }

        public DllInjectionResult Inject(DetectedGame detectedGame)
        {
            return _gameSession.Inject(detectedGame);
        }

        public GameConnectionConnectResult CompleteConnect(
            DetectedGame detectedGame,
            DllInjectionResult injectionResult)
        {
            GameEventMonitorStatus eventStatus = GameEventMonitorStatus.WaitingForMonitor;
            DateTimeOffset? attemptedAt = null;
            if (IsMonitorLoadedInjectionState(injectionResult.State))
            {
                attemptedAt = _timeProvider.GetUtcNow();
                eventStatus = _gameSession.ReadEventMonitorStatus(attemptedAt.Value, detectedGame.ProcessId);
            }

            _lastInjectionProcessId = detectedGame.ProcessId;
            _lastInjectionResult = injectionResult;
            _lastInjectionAttemptedAt = attemptedAt;
            IsConnecting = false;

            return new GameConnectionConnectResult(injectionResult, eventStatus);
        }

        public bool TryBeginDisconnect()
        {
            if (IsDisconnecting)
            {
                return true;
            }

            int? monitorProcessId = _lastInjectionProcessId;
            if (monitorProcessId is null || !IsMonitorLoadedInjectionState(_lastInjectionResult.State))
            {
                return false;
            }

            IsConnecting = false;
            IsDisconnecting = true;
            _disconnectProcessId = monitorProcessId;
            _disconnectRequestedAt = _timeProvider.GetUtcNow();
            _gameSession.RequestMonitorStop(monitorProcessId);
            return true;
        }

        public bool IsMonitorDisconnectComplete()
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
                && _timeProvider.GetUtcNow() - requestedAt >= MonitorDisconnectTimeout;
        }

        public void CompleteDisconnect()
        {
            ResetMonitorConnectionState(requestStop: false);
        }

        public void ResetMonitorConnectionState(bool requestStop = true)
        {
            int? monitorProcessId = _lastInjectionProcessId;
            IsConnecting = false;
            IsDisconnecting = false;
            _disconnectProcessId = null;
            _disconnectRequestedAt = null;
            _lastInjectionProcessId = null;
            _lastInjectionAttemptedAt = null;
            _lastInjectionResult = DllInjectionResult.NotAttempted;
            if (requestStop)
            {
                _gameSession.RequestMonitorStop(monitorProcessId);
            }
        }

        public void Dispose()
        {
            _gameSession.Dispose();
        }

        private static bool IsMonitorLoadedInjectionState(DllInjectionState state)
        {
            return state is DllInjectionState.Loaded or DllInjectionState.AlreadyInjected;
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
    }

    internal readonly record struct GameConnectionConnectResult(
        DllInjectionResult InjectionResult,
        GameEventMonitorStatus EventStatus);

    internal readonly record struct GameConnectionRefreshResult(
        PlayerStatsReadResult ReadResult,
        GameEventMonitorStatus EventStatus,
        DllInjectionResult InjectionResult);
}
