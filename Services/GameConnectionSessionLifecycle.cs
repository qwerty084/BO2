using System;

namespace BO2.Services
{
    internal sealed class GameConnectionSessionLifecycle
    {
        private bool _isConnecting;
        private bool _isDisconnecting;
        private DllInjectionResult _lastInjectionResult = DllInjectionResult.NotAttempted;
        private int? _lastInjectionProcessId;
        private DateTimeOffset? _lastInjectionAttemptedAt;
        private GameConnectionSessionLifecycleGame? _connectTargetGame;
        private int? _disconnectProcessId;
        private DateTimeOffset? _disconnectRequestedAt;

        public bool IsConnecting => _isConnecting;

        public bool IsDisconnecting => _isDisconnecting;

        public int? DisconnectProcessId => _disconnectProcessId;

        public DateTimeOffset? DisconnectRequestedAt => _disconnectRequestedAt;

        public GameConnectionSessionLifecycleGame? ConnectTargetGame => _connectTargetGame;

        public bool BeginConnect(GameConnectionSessionLifecycleGame? detectedGame)
        {
            if (_isConnecting)
            {
                return false;
            }

            if (!CanAttemptConnect(detectedGame))
            {
                _connectTargetGame = null;
                return false;
            }

            _connectTargetGame = detectedGame;
            _isConnecting = true;
            return true;
        }

        public bool CanCompleteConnectFor(GameConnectionSessionLifecycleGame? detectedGame)
        {
            return _isConnecting
                && _connectTargetGame is GameConnectionSessionLifecycleGame connectTargetGame
                && detectedGame == connectTargetGame;
        }

        public GameConnectionSessionConnectCompletion CompleteConnect(
            GameConnectionSessionLifecycleGame? detectedGame,
            GameConnectionSessionLifecycleGame? connectTargetGame,
            DllInjectionResult injectionResult,
            DateTimeOffset receivedAt)
        {
            ArgumentNullException.ThrowIfNull(injectionResult);

            GameConnectionSessionLifecycleGame? attemptedTargetGame = connectTargetGame ?? _connectTargetGame;
            bool isTargetMatch = CanCompleteConnectFor(detectedGame);
            if (isTargetMatch
                && _connectTargetGame is GameConnectionSessionLifecycleGame matchedTargetGame)
            {
                _lastInjectionProcessId = matchedTargetGame.ProcessId;
                _lastInjectionResult = injectionResult;
                _lastInjectionAttemptedAt = IsMonitorLoadedInjectionState(injectionResult.State)
                    ? receivedAt
                    : null;
            }

            _connectTargetGame = null;
            _isConnecting = false;
            return new GameConnectionSessionConnectCompletion(
                isTargetMatch,
                isTargetMatch
                    ? GameConnectionSessionMonitorStopRequest.None
                    : CreateMismatchedConnectStopRequest(attemptedTargetGame, injectionResult));
        }

        private static GameConnectionSessionMonitorStopRequest CreateMismatchedConnectStopRequest(
            GameConnectionSessionLifecycleGame? connectTargetGame,
            DllInjectionResult injectionResult)
        {
            return connectTargetGame is GameConnectionSessionLifecycleGame targetGame
                && IsMonitorLoadedInjectionState(injectionResult.State)
                    ? new GameConnectionSessionMonitorStopRequest(
                        targetGame.ProcessId,
                        ShouldRequestStop: true)
                    : GameConnectionSessionMonitorStopRequest.None;
        }

        public void CancelConnect()
        {
            if (!_isConnecting)
            {
                return;
            }

            _connectTargetGame = null;
            _isConnecting = false;
        }

        public GameConnectionSessionMonitorStopRequest RollbackFailedConnect(
            GameConnectionSessionLifecycleGame? connectTargetGame)
        {
            if (_isConnecting)
            {
                CancelConnect();
                return GameConnectionSessionMonitorStopRequest.None;
            }

            if (connectTargetGame is GameConnectionSessionLifecycleGame targetGame
                && _lastInjectionProcessId == targetGame.ProcessId)
            {
                return ResetMonitorConnectionState(IsMonitorLoaded);
            }

            return GameConnectionSessionMonitorStopRequest.None;
        }

        public GameConnectionSessionMonitorStopRequest ResetForDetectedGameChange(
            GameConnectionSessionLifecycleGame? currentGame,
            GameConnectionSessionLifecycleGame? detectedGame)
        {
            if (currentGame == detectedGame)
            {
                return GameConnectionSessionMonitorStopRequest.None;
            }

            return ResetMonitorConnectionState();
        }

        public GameConnectionSessionDisconnectAction BeginDisconnect(DateTimeOffset receivedAt)
        {
            if (_isDisconnecting)
            {
                return GameConnectionSessionDisconnectAction.CreateDisconnectingSnapshot;
            }

            if (_lastInjectionProcessId is not int monitorProcessId || !IsMonitorLoaded)
            {
                ResetMonitorConnectionState(requestStop: false);
                return GameConnectionSessionDisconnectAction.ReadSnapshot;
            }

            _isConnecting = false;
            _connectTargetGame = null;
            _isDisconnecting = true;
            _disconnectProcessId = monitorProcessId;
            _disconnectRequestedAt = receivedAt;
            return GameConnectionSessionDisconnectAction.RequestStop(monitorProcessId);
        }

        public GameConnectionSessionDisconnectRefreshAction RefreshDisconnect()
        {
            if (!_isDisconnecting)
            {
                return GameConnectionSessionDisconnectRefreshAction.ReadSnapshot;
            }

            if (_disconnectProcessId is not int monitorProcessId)
            {
                ResetMonitorConnectionState(requestStop: false);
                return GameConnectionSessionDisconnectRefreshAction.ReadSnapshot;
            }

            return GameConnectionSessionDisconnectRefreshAction.CheckStopComplete(monitorProcessId);
        }

        public GameConnectionSessionDisconnectRefreshAction CompleteDisconnectStopCheck(
            int monitorProcessId,
            bool isStopComplete,
            DateTimeOffset receivedAt,
            TimeSpan disconnectTimeout)
        {
            if (!_isDisconnecting || _disconnectProcessId != monitorProcessId)
            {
                return GameConnectionSessionDisconnectRefreshAction.ReadSnapshot;
            }

            if (isStopComplete || HasDisconnectTimedOut(receivedAt, disconnectTimeout))
            {
                ResetMonitorConnectionState(requestStop: false);
                return GameConnectionSessionDisconnectRefreshAction.ReadSnapshot;
            }

            return GameConnectionSessionDisconnectRefreshAction.CreateDisconnectingSnapshot(monitorProcessId);
        }

        public GameConnectionSessionMonitorStopRequest ResetMonitorConnectionState(bool requestStop = true)
        {
            int? monitorProcessId = _lastInjectionProcessId;
            bool stopAlreadyRequested = _isDisconnecting
                && monitorProcessId is not null
                && _disconnectProcessId == monitorProcessId;
            ClearTransientOperationState();
            _lastInjectionProcessId = null;
            _lastInjectionAttemptedAt = null;
            _lastInjectionResult = DllInjectionResult.NotAttempted;
            return new GameConnectionSessionMonitorStopRequest(
                monitorProcessId,
                requestStop && !stopAlreadyRequested && monitorProcessId is not null);
        }

        public void ClearTransientOperationState()
        {
            _isConnecting = false;
            _isDisconnecting = false;
            _connectTargetGame = null;
            _disconnectProcessId = null;
            _disconnectRequestedAt = null;
        }

        public GameConnectionSessionMonitorStopRequest ApplyMonitorReadinessTimeout(
            GameConnectionSessionLifecycleGame? detectedGame,
            GameEventMonitorStatus eventStatus,
            DateTimeOffset now,
            TimeSpan retryTimeout,
            string timeoutMessage)
        {
            if (detectedGame is not GameConnectionSessionLifecycleGame game
                || _lastInjectionProcessId != game.ProcessId
                || eventStatus.CompatibilityState != GameCompatibilityState.WaitingForMonitor
                || !IsMonitorLoaded
                || _lastInjectionAttemptedAt is not DateTimeOffset attemptedAt
                || now - attemptedAt < retryTimeout)
            {
                return GameConnectionSessionMonitorStopRequest.None;
            }

            int? monitorProcessId = _lastInjectionProcessId;
            _lastInjectionResult = new DllInjectionResult(
                DllInjectionState.Failed,
                timeoutMessage);
            _lastInjectionAttemptedAt = null;
            return new GameConnectionSessionMonitorStopRequest(monitorProcessId, ShouldRequestStop: true);
        }

        public GameConnectionSessionLifecycleSnapshot CreateSnapshot(
            GameConnectionSessionLifecycleGame? detectedGame)
        {
            return new GameConnectionSessionLifecycleSnapshot(
                _lastInjectionResult,
                _isConnecting,
                _isDisconnecting,
                CanAttemptConnect(detectedGame),
                HasInjectionAttemptFor(detectedGame),
                IsMonitorConnectedFor(detectedGame));
        }

        public bool IsMonitorConnectedFor(GameConnectionSessionLifecycleGame? detectedGame)
        {
            return detectedGame is GameConnectionSessionLifecycleGame game
                && _lastInjectionProcessId == game.ProcessId
                && IsMonitorLoaded;
        }

        private bool CanAttemptConnect(GameConnectionSessionLifecycleGame? detectedGame)
        {
            return !_isConnecting
                && !_isDisconnecting
                && detectedGame is GameConnectionSessionLifecycleGame game
                && game.Variant == GameVariant.SteamZombies
                && game.IsStatsSupported
                && !IsMonitorConnectedFor(detectedGame);
        }

        private bool HasInjectionAttemptFor(GameConnectionSessionLifecycleGame? detectedGame)
        {
            return detectedGame is GameConnectionSessionLifecycleGame game
                && _lastInjectionProcessId == game.ProcessId
                && _lastInjectionResult.State != DllInjectionState.NotAttempted;
        }

        private bool IsMonitorLoaded => IsMonitorLoadedInjectionState(_lastInjectionResult.State);

        private bool HasDisconnectTimedOut(DateTimeOffset receivedAt, TimeSpan disconnectTimeout)
        {
            return _disconnectRequestedAt is DateTimeOffset requestedAt
                && receivedAt - requestedAt >= disconnectTimeout;
        }

        private static bool IsMonitorLoadedInjectionState(DllInjectionState state)
        {
            return state is DllInjectionState.Loaded or DllInjectionState.AlreadyInjected;
        }
    }

    internal readonly record struct GameConnectionSessionLifecycleGame(
        int ProcessId,
        GameVariant Variant,
        bool IsStatsSupported)
    {
        public static GameConnectionSessionLifecycleGame? FromDetectedGame(DetectedGame? detectedGame)
        {
            return detectedGame is null
                ? null
                : new GameConnectionSessionLifecycleGame(
                    detectedGame.ProcessId,
                    detectedGame.Variant,
                    detectedGame.IsStatsSupported);
        }
    }

    internal readonly record struct GameConnectionSessionLifecycleSnapshot(
        DllInjectionResult InjectionResult,
        bool IsConnecting,
        bool IsDisconnecting,
        bool CanAttemptConnect,
        bool HasInjectionAttemptForCurrentGame,
        bool IsMonitorConnectedForCurrentGame);

    internal readonly record struct GameConnectionSessionConnectCompletion(
        bool IsTargetMatch,
        GameConnectionSessionMonitorStopRequest StopRequest);

    internal readonly record struct GameConnectionSessionMonitorStopRequest(
        int? MonitorProcessId,
        bool ShouldRequestStop)
    {
        public static GameConnectionSessionMonitorStopRequest None { get; } = new(
            null,
            ShouldRequestStop: false);
    }

    internal readonly record struct GameConnectionSessionDisconnectAction(
        bool ShouldReadSnapshot,
        bool ShouldRequestStop,
        int? MonitorProcessId)
    {
        public static GameConnectionSessionDisconnectAction ReadSnapshot { get; } = new(
            ShouldReadSnapshot: true,
            ShouldRequestStop: false,
            MonitorProcessId: null);

        public static GameConnectionSessionDisconnectAction CreateDisconnectingSnapshot { get; } = new(
            ShouldReadSnapshot: false,
            ShouldRequestStop: false,
            MonitorProcessId: null);

        public static GameConnectionSessionDisconnectAction RequestStop(int monitorProcessId)
        {
            return new GameConnectionSessionDisconnectAction(
                ShouldReadSnapshot: false,
                ShouldRequestStop: true,
                monitorProcessId);
        }
    }

    internal readonly record struct GameConnectionSessionDisconnectRefreshAction(
        bool ShouldReadSnapshot,
        bool ShouldCheckStopComplete,
        int? MonitorProcessId)
    {
        public static GameConnectionSessionDisconnectRefreshAction ReadSnapshot { get; } = new(
            ShouldReadSnapshot: true,
            ShouldCheckStopComplete: false,
            MonitorProcessId: null);

        public static GameConnectionSessionDisconnectRefreshAction CreateDisconnectingSnapshot(int monitorProcessId)
        {
            return new GameConnectionSessionDisconnectRefreshAction(
                ShouldReadSnapshot: false,
                ShouldCheckStopComplete: false,
                monitorProcessId);
        }

        public static GameConnectionSessionDisconnectRefreshAction CheckStopComplete(int monitorProcessId)
        {
            return new GameConnectionSessionDisconnectRefreshAction(
                ShouldReadSnapshot: false,
                ShouldCheckStopComplete: true,
                monitorProcessId);
        }
    }
}
