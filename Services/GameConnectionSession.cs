using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        private GameConnectionSnapshot _snapshot;
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
            _snapshot = CreateSnapshot(CreateStatusSnapshotLocked(_currentGame));
            _processDetectionService.DetectedGameChanged += OnDetectedGameChanged;
        }

        public event EventHandler<GameConnectionSnapshotChangedEventArgs>? SnapshotChanged;

        public GameConnectionSnapshot Snapshot
        {
            get
            {
                lock (_syncRoot)
                {
                    return _snapshot;
                }
            }
        }

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

            ApplyDetectedGame(_processDetectionService.CurrentGame);
        }

        public GameConnectionSnapshot Read()
        {
            long diagnosticsStartedAt = RefreshDiagnostics.Start();
            try
            {
                DateTimeOffset receivedAt = _timeProvider.GetUtcNow();
                DetectedGame? detectedGame = RefreshCurrentGame();
                if (IsDisconnecting)
                {
                    return PublishSnapshot(ReadDisconnectSnapshot(detectedGame, receivedAt));
                }

                return PublishSnapshot(ReadSnapshot(detectedGame, receivedAt));
            }
            catch (Exception ex) when (IsRecoverableReadException(ex))
            {
                HandleReadFailure();
                throw;
            }
            finally
            {
                RefreshDiagnostics.WriteElapsed("game connection refresh", diagnosticsStartedAt);
            }
        }

        public GameConnectionSnapshot GetStatusSnapshot()
        {
            RefreshCurrentGame();
            GameConnectionRefreshResult result;
            lock (_syncRoot)
            {
                result = CreateStatusSnapshotLocked(_currentGame);
            }

            return PublishSnapshot(result);
        }

        public GameConnectionSnapshot HandleReadFailure()
        {
            RefreshCurrentGame();
            GameConnectionRefreshResult result;
            lock (_syncRoot)
            {
                _lifecycle.ClearTransientOperationState();
                result = CreateStatusSnapshotLocked(_currentGame);
            }

            return PublishSnapshot(result);
        }

        internal bool IsMonitorConnectedFor(DetectedGame? detectedGame)
        {
            lock (_syncRoot)
            {
                return _lifecycle.IsMonitorConnectedFor(
                    GameConnectionSessionLifecycleGame.FromDetectedGame(detectedGame));
            }
        }

        public GameConnectionSnapshot Connect()
        {
            GameConnectionRefreshResult connectingResult = BeginConnect();
            if (!connectingResult.IsConnecting)
            {
                return CreateSnapshot(connectingResult);
            }

            return CreateSnapshot(CompleteConnect());
        }

        public GameConnectionSnapshot Disconnect()
        {
            return CreateSnapshot(BeginDisconnect());
        }

        private GameConnectionRefreshResult BeginConnect()
        {
            _ = RefreshCurrentGame();
            GameConnectionRefreshResult result;
            lock (_syncRoot)
            {
                DetectedGame? detectedGame = _currentGame;
                _lifecycle.BeginConnect(
                    GameConnectionSessionLifecycleGame.FromDetectedGame(detectedGame));
                result = CreateStatusSnapshotLocked(detectedGame);
            }

            return PublishRefreshResult(result);
        }

        private GameConnectionRefreshResult CompleteConnect()
        {
            GameConnectionSessionLifecycleGame? connectTargetGame = GetConnectTargetGame();
            try
            {
                return PublishRefreshResult(CompleteConnect(connectTargetGame, Inject()));
            }
            catch
            {
                RollbackFailedConnect(connectTargetGame);
                throw;
            }
        }

        private void RollbackFailedConnect(GameConnectionSessionLifecycleGame? connectTargetGame)
        {
            GameConnectionSessionMonitorStopRequest stopRequest;
            GameConnectionRefreshResult snapshot;
            lock (_syncRoot)
            {
                stopRequest = _lifecycle.RollbackFailedConnect(connectTargetGame);
                snapshot = CreateStatusSnapshotLocked(_currentGame);
            }

            if (stopRequest.ShouldRequestStop)
            {
                _eventMonitor.RequestStop(stopRequest.MonitorProcessId);
            }

            PublishSnapshot(snapshot);
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

        private GameConnectionRefreshResult CompleteConnect(
            GameConnectionSessionLifecycleGame? connectTargetGame,
            DllInjectionResult injectionResult)
        {
            ArgumentNullException.ThrowIfNull(injectionResult);

            DateTimeOffset receivedAt = _timeProvider.GetUtcNow();
            DetectedGame? detectedGame = RefreshCurrentGame();
            GameConnectionSessionConnectCompletion completion;
            lock (_syncRoot)
            {
                completion = _lifecycle.CompleteConnect(
                    GameConnectionSessionLifecycleGame.FromDetectedGame(detectedGame),
                    connectTargetGame,
                    injectionResult,
                    receivedAt);
            }

            if (completion.StopRequest.ShouldRequestStop)
            {
                _eventMonitor.RequestStop(completion.StopRequest.MonitorProcessId);
            }

            return ReadSnapshot(detectedGame, receivedAt);
        }

        private GameConnectionRefreshResult BeginDisconnect()
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

            GameConnectionRefreshResult snapshot = disconnectAction.ShouldReadSnapshot
                ? ReadSnapshot(detectedGame, receivedAt)
                : result!.Value;

            return PublishRefreshResult(snapshot);
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
            GameConnectionSessionDisconnectRefreshAction disconnectAction;
            GameConnectionRefreshResult? result = null;
            lock (_syncRoot)
            {
                disconnectAction = _lifecycle.RefreshDisconnect();
                if (!disconnectAction.ShouldReadSnapshot && !disconnectAction.ShouldCheckStopComplete)
                {
                    result = CreateDisconnectingSnapshotLocked(detectedGame);
                }
            }

            if (disconnectAction.ShouldCheckStopComplete
                && disconnectAction.MonitorProcessId is int monitorProcessId)
            {
                bool isStopComplete = _eventMonitor.IsStopComplete(monitorProcessId);
                lock (_syncRoot)
                {
                    disconnectAction = _lifecycle.CompleteDisconnectStopCheck(
                        monitorProcessId,
                        isStopComplete,
                        receivedAt,
                        MonitorDisconnectTimeout);
                    if (!disconnectAction.ShouldReadSnapshot && !disconnectAction.ShouldCheckStopComplete)
                    {
                        result = CreateDisconnectingSnapshotLocked(detectedGame);
                    }
                }
            }

            return disconnectAction.ShouldReadSnapshot
                ? ReadSnapshot(detectedGame, receivedAt)
                : result!.Value;
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
            ApplyDetectedGame(args.DetectedGame);
        }

        private DetectedGame? RefreshCurrentGame()
        {
            if (UsesPollingProcessDetection)
            {
                ApplyDetectedGame(DetectByPolling());
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
            PlayerStatsReadResult? readResult = ReadPlayerStats(detectedGame);
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
                    && readResult?.DetectedGame.ProcessId == detectedGame.ProcessId
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

                stopRequest = ApplyMonitorReadinessTimeoutLocked(readResult?.DetectedGame, eventStatus, receivedAt);
                result = CreateRefreshResultLocked(detectedGame, readResult, eventStatus);
            }

            if (stopRequest.ShouldRequestStop)
            {
                _eventMonitor.RequestStop(stopRequest.MonitorProcessId);
            }

            return result;
        }

        private PlayerStatsReadResult? ReadPlayerStats(DetectedGame? detectedGame)
        {
            if (detectedGame?.AddressMap is null)
            {
                _memoryReader.ClearAttachedGame();
                return null;
            }

            return _memoryReader.ReadPlayerStats(detectedGame);
        }

        private void ApplyDetectedGame(DetectedGame? detectedGame)
        {
            GameConnectionSnapshotChangedEventArgs? snapshotChangedArgs;
            GameConnectionSessionMonitorStopRequest stopRequest;
            lock (_syncRoot)
            {
                if (Equals(_currentGame, detectedGame))
                {
                    return;
                }

                GameConnectionSessionLifecycleGame? currentLifecycleGame =
                    GameConnectionSessionLifecycleGame.FromDetectedGame(_currentGame);
                GameConnectionSessionLifecycleGame? detectedLifecycleGame =
                    GameConnectionSessionLifecycleGame.FromDetectedGame(detectedGame);
                _currentGame = detectedGame;
                stopRequest = _lifecycle.ResetForDetectedGameChange(
                    currentLifecycleGame,
                    detectedLifecycleGame);
                snapshotChangedArgs = UpdateSnapshotLocked(
                    CreateSnapshot(CreateStatusSnapshotLocked(detectedGame)));
            }

            if (stopRequest.ShouldRequestStop)
            {
                _eventMonitor.RequestStop(stopRequest.MonitorProcessId);
            }

            RaiseSnapshotChanged(snapshotChangedArgs);
        }

        private GameConnectionRefreshResult PublishRefreshResult(GameConnectionRefreshResult result)
        {
            PublishSnapshot(result);
            return result;
        }

        private GameConnectionSnapshot PublishSnapshot(GameConnectionRefreshResult result)
        {
            GameConnectionSnapshot snapshot = CreateSnapshot(result);
            GameConnectionSnapshotChangedEventArgs? args;
            lock (_syncRoot)
            {
                args = UpdateSnapshotLocked(snapshot);
            }

            RaiseSnapshotChanged(args);
            return snapshot;
        }

        private GameConnectionSnapshotChangedEventArgs? UpdateSnapshotLocked(GameConnectionSnapshot snapshot)
        {
            if (HasSameObservableState(_snapshot, snapshot))
            {
                return null;
            }

            GameConnectionSnapshot previousSnapshot = _snapshot;
            _snapshot = snapshot;
            return new GameConnectionSnapshotChangedEventArgs(previousSnapshot, snapshot);
        }

        private void RaiseSnapshotChanged(GameConnectionSnapshotChangedEventArgs? args)
        {
            if (args is not null)
            {
                SnapshotChanged?.Invoke(this, args);
            }
        }

        private static bool HasSameObservableState(
            GameConnectionSnapshot left,
            GameConnectionSnapshot right)
        {
            return Equals(left.CurrentGame, right.CurrentGame)
                && left.ConnectionPhase == right.ConnectionPhase
                && Equals(left.ReadResult, right.ReadResult)
                && Equals(left.InjectionResult, right.InjectionResult)
                && left.IsConnecting == right.IsConnecting
                && left.IsDisconnecting == right.IsDisconnecting
                && left.CanAttemptConnect == right.CanAttemptConnect
                && left.HasInjectionAttemptForCurrentGame == right.HasInjectionAttemptForCurrentGame
                && left.IsMonitorConnectedForCurrentGame == right.IsMonitorConnectedForCurrentGame
                && HasSameEventStatus(left.EventStatus, right.EventStatus);
        }

        private static bool HasSameEventStatus(
            GameEventMonitorStatus left,
            GameEventMonitorStatus right)
        {
            return left.CompatibilityState == right.CompatibilityState
                && left.DroppedEventCount == right.DroppedEventCount
                && left.DroppedNotifyCount == right.DroppedNotifyCount
                && left.PublishedNotifyCount == right.PublishedNotifyCount
                && HasSameEvents(left.RecentEvents, right.RecentEvents);
        }

        private static bool HasSameEvents(IReadOnlyList<GameEvent> left, IReadOnlyList<GameEvent> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!Equals(left[i], right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsRecoverableReadException(Exception exception)
        {
            return exception is InvalidOperationException or Win32Exception;
        }

        private GameConnectionRefreshResult CreateStatusSnapshotLocked(DetectedGame? detectedGame)
        {
            return CreateRefreshResultLocked(
                detectedGame,
                null,
                GameEventMonitorStatus.WaitingForMonitor);
        }

        private GameConnectionRefreshResult CreateDisconnectingSnapshotLocked(DetectedGame? detectedGame)
        {
            return CreateRefreshResultLocked(
                detectedGame,
                null,
                GameEventMonitorStatus.WaitingForMonitor);
        }

        private GameConnectionRefreshResult CreateRefreshResultLocked(
            DetectedGame? detectedGame,
            PlayerStatsReadResult? readResult,
            GameEventMonitorStatus eventStatus)
        {
            GameConnectionSessionLifecycleSnapshot lifecycleSnapshot = _lifecycle.CreateSnapshot(
                GameConnectionSessionLifecycleGame.FromDetectedGame(detectedGame));
            return new GameConnectionRefreshResult(
                detectedGame,
                DetermineConnectionPhase(detectedGame, readResult, lifecycleSnapshot),
                readResult,
                eventStatus,
                lifecycleSnapshot.InjectionResult,
                lifecycleSnapshot.IsConnecting,
                lifecycleSnapshot.IsDisconnecting,
                lifecycleSnapshot.CanAttemptConnect,
                lifecycleSnapshot.HasInjectionAttemptForCurrentGame,
                lifecycleSnapshot.IsMonitorConnectedForCurrentGame);
        }

        private static GameConnectionPhase DetermineConnectionPhase(
            DetectedGame? detectedGame,
            PlayerStatsReadResult? readResult,
            GameConnectionSessionLifecycleSnapshot lifecycleSnapshot)
        {
            if (detectedGame is null)
            {
                return GameConnectionPhase.NoGame;
            }

            if (!detectedGame.IsStatsSupported)
            {
                return GameConnectionPhase.UnsupportedGame;
            }

            if (lifecycleSnapshot.IsDisconnecting)
            {
                return GameConnectionPhase.Disconnecting;
            }

            if (lifecycleSnapshot.IsConnecting)
            {
                return GameConnectionPhase.Connecting;
            }

            if (lifecycleSnapshot.IsMonitorConnectedForCurrentGame)
            {
                return GameConnectionPhase.Connected;
            }

            return readResult?.DetectedGame.ProcessId == detectedGame.ProcessId
                && readResult.Stats is not null
                    ? GameConnectionPhase.StatsOnlyDetected
                    : GameConnectionPhase.Detected;
        }

        private static GameConnectionSnapshot CreateSnapshot(GameConnectionRefreshResult result)
        {
            return new GameConnectionSnapshot(
                result.CurrentGame,
                result.ConnectionPhase,
                result.ReadResult,
                result.EventStatus,
                result.InjectionResult,
                result.IsConnecting,
                result.IsDisconnecting,
                result.CanAttemptConnect,
                result.HasInjectionAttemptForCurrentGame,
                result.IsMonitorConnectedForCurrentGame);
        }

        private readonly record struct GameConnectionRefreshResult(
            DetectedGame? CurrentGame,
            GameConnectionPhase ConnectionPhase,
            PlayerStatsReadResult? ReadResult,
            GameEventMonitorStatus EventStatus,
            DllInjectionResult InjectionResult,
            bool IsConnecting,
            bool IsDisconnecting,
            bool CanAttemptConnect,
            bool HasInjectionAttemptForCurrentGame,
            bool IsMonitorConnectedForCurrentGame);
    }

    internal readonly record struct GameConnectionSnapshot(
        DetectedGame? CurrentGame,
        GameConnectionPhase ConnectionPhase,
        PlayerStatsReadResult? ReadResult,
        GameEventMonitorStatus EventStatus,
        DllInjectionResult InjectionResult,
        bool IsConnecting,
        bool IsDisconnecting,
        bool CanAttemptConnect,
        bool HasInjectionAttemptForCurrentGame,
        bool IsMonitorConnectedForCurrentGame);

    internal sealed class GameConnectionSnapshotChangedEventArgs(
        GameConnectionSnapshot previousSnapshot,
        GameConnectionSnapshot snapshot) : EventArgs
    {
        public GameConnectionSnapshot PreviousSnapshot { get; } = previousSnapshot;

        public GameConnectionSnapshot Snapshot { get; } = snapshot;
    }
}
