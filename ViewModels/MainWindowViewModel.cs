using BO2.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace BO2.ViewModels
{
    public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
    {
        private const string EmptyStatText = "--";
        private static readonly TimeSpan MonitorReadinessRetryTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan MonitorDisconnectTimeout = TimeSpan.FromSeconds(3);

        private readonly DispatcherQueue _dispatcherQueue;
        private readonly GameSessionCoordinator _gameSession;
        private readonly StatsRefreshService _statsRefreshService;
        private readonly SemaphoreSlim _operationSemaphore = new(1, 1);
        private DllInjectionResult _lastInjectionResult = DllInjectionResult.NotAttempted;
        private int? _lastInjectionProcessId;
        private DateTimeOffset? _lastInjectionAttemptedAt;
        private DetectedGame? _detectedGame;
        private bool _isConnecting;
        private bool _isDisconnecting;
        private int? _disconnectProcessId;
        private DateTimeOffset? _disconnectRequestedAt;
        private bool _disposed;
        private string _pointsText = EmptyStatText;
        private string _killsText = EmptyStatText;
        private string _downsText = EmptyStatText;
        private string _revivesText = EmptyStatText;
        private string _headshotsText = EmptyStatText;
        private string _positionXText = EmptyStatText;
        private string _positionYText = EmptyStatText;
        private string _positionZText = EmptyStatText;
        private string _playerCandidateDetailsText = EmptyStatText;
        private string _ammoCandidateDetailsText = EmptyStatText;
        private string _counterCandidateDetailsText = EmptyStatText;
        private string _addressCandidateDetailsText = EmptyStatText;
        private string _detectedGameText = AppStrings.Get("NoGameDetected");
        private string _eventCompatibilityText = AppStrings.Get("NoGameDetected");
        private string _injectionStatusText = AppStrings.Get("DllInjectionNotAttempted");
        private string _eventMonitorStatusText = AppStrings.Get("EventMonitorWaitingForMonitor");
        private string _currentRoundText = EmptyStatText;
        private string _boxEventsText = AppStrings.Get("RecentEventsEmpty");
        private string _recentGameEventsText = AppStrings.Get("RecentEventsEmpty");
        private string _statusText = AppStrings.Get("GameNotRunning");
        private string _gameStatusText = AppStrings.Get("FooterGameNotRunning");
        private string _eventConnectionStatusText = AppStrings.Get("FooterEventsNotConnected");
        private string _connectButtonText = AppStrings.Get("ConnectButtonText");
        private string _disconnectButtonText = AppStrings.Get("DisconnectButtonText");
        private string _connectionCardStatusText = AppStrings.Get("ConnectionCardStatusDisconnected");
        private string _connectionLastUpdateText = EmptyStatText;
        private bool _isConnectButtonEnabled;
        private Visibility _connectButtonVisibility = Visibility.Visible;
        private Visibility _disconnectButtonVisibility = Visibility.Collapsed;
        private Visibility _footerSuccessStatusVisibility = Visibility.Collapsed;
        private Visibility _footerPendingStatusVisibility = Visibility.Collapsed;
        private Visibility _footerDisconnectedStatusVisibility = Visibility.Visible;
        private Visibility _footerErrorStatusVisibility = Visibility.Collapsed;
        private readonly StatFormatter _formatter;
        private GameEventMonitorStatus _latestEventStatus = GameEventMonitorStatus.WaitingForMonitor;

        public MainWindowViewModel(DispatcherQueue dispatcherQueue)
            : this(dispatcherQueue, new GameSessionCoordinator())
        {
        }

        internal MainWindowViewModel(DispatcherQueue dispatcherQueue, GameSessionCoordinator gameSession)
        {
            ArgumentNullException.ThrowIfNull(dispatcherQueue);
            ArgumentNullException.ThrowIfNull(gameSession);

            _dispatcherQueue = dispatcherQueue;
            _gameSession = gameSession;
            _statsRefreshService = new StatsRefreshService(_gameSession);
            _formatter = new StatFormatter(AppStrings.Get("UnavailableValue"));
            _gameSession.DetectedGameChanged += OnDetectedGameChanged;

            _gameSession.Start();
            _detectedGame = _gameSession.CurrentGame;
            ApplyConnectionStatus(_detectedGame);
            UpdateConnectButtonState(_detectedGame);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public event EventHandler<GameEventMonitorStatus>? EventStatusUpdated;

        public event EventHandler? RefreshRequested;

        public string PointsText
        {
            get => _pointsText;
            private set => SetProperty(ref _pointsText, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public string GameStatusText
        {
            get => _gameStatusText;
            private set => SetProperty(ref _gameStatusText, value);
        }

        public string EventConnectionStatusText
        {
            get => _eventConnectionStatusText;
            private set => SetProperty(ref _eventConnectionStatusText, value);
        }

        public string DetectedGameText
        {
            get => _detectedGameText;
            private set => SetProperty(ref _detectedGameText, value);
        }

        public string EventCompatibilityText
        {
            get => _eventCompatibilityText;
            private set => SetProperty(ref _eventCompatibilityText, value);
        }

        public string InjectionStatusText
        {
            get => _injectionStatusText;
            private set => SetProperty(ref _injectionStatusText, value);
        }

        public string ConnectButtonText
        {
            get => _connectButtonText;
            private set => SetProperty(ref _connectButtonText, value);
        }

        public string DisconnectButtonText
        {
            get => _disconnectButtonText;
            private set => SetProperty(ref _disconnectButtonText, value);
        }

        public string ConnectionCardStatusText
        {
            get => _connectionCardStatusText;
            private set => SetProperty(ref _connectionCardStatusText, value);
        }

        public string ConnectionLastUpdateText
        {
            get => _connectionLastUpdateText;
            private set => SetProperty(ref _connectionLastUpdateText, value);
        }

        public bool IsConnectButtonEnabled
        {
            get => _isConnectButtonEnabled;
            private set => SetProperty(ref _isConnectButtonEnabled, value);
        }

        public Visibility ConnectButtonVisibility
        {
            get => _connectButtonVisibility;
            private set => SetProperty(ref _connectButtonVisibility, value);
        }

        public Visibility DisconnectButtonVisibility
        {
            get => _disconnectButtonVisibility;
            private set => SetProperty(ref _disconnectButtonVisibility, value);
        }

        public string EventMonitorStatusText
        {
            get => _eventMonitorStatusText;
            private set => SetProperty(ref _eventMonitorStatusText, value);
        }

        public string RecentGameEventsText
        {
            get => _recentGameEventsText;
            private set => SetProperty(ref _recentGameEventsText, value);
        }

        public string CurrentRoundText
        {
            get => _currentRoundText;
            private set => SetProperty(ref _currentRoundText, value);
        }

        public string BoxEventsText
        {
            get => _boxEventsText;
            private set => SetProperty(ref _boxEventsText, value);
        }

        public GameEventMonitorStatus LatestEventStatus
        {
            get => _latestEventStatus;
            private set
            {
                if (Equals(_latestEventStatus, value))
                {
                    return;
                }

                _latestEventStatus = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LatestEventStatus)));
                EventStatusUpdated?.Invoke(this, value);
            }
        }

        public Visibility FooterSuccessStatusVisibility
        {
            get => _footerSuccessStatusVisibility;
            private set => SetProperty(ref _footerSuccessStatusVisibility, value);
        }

        public Visibility FooterPendingStatusVisibility
        {
            get => _footerPendingStatusVisibility;
            private set => SetProperty(ref _footerPendingStatusVisibility, value);
        }

        public Visibility FooterDisconnectedStatusVisibility
        {
            get => _footerDisconnectedStatusVisibility;
            private set => SetProperty(ref _footerDisconnectedStatusVisibility, value);
        }

        public Visibility FooterErrorStatusVisibility
        {
            get => _footerErrorStatusVisibility;
            private set => SetProperty(ref _footerErrorStatusVisibility, value);
        }

        public string KillsText
        {
            get => _killsText;
            private set => SetProperty(ref _killsText, value);
        }

        public string DownsText
        {
            get => _downsText;
            private set => SetProperty(ref _downsText, value);
        }

        public string RevivesText
        {
            get => _revivesText;
            private set => SetProperty(ref _revivesText, value);
        }

        public string HeadshotsText
        {
            get => _headshotsText;
            private set => SetProperty(ref _headshotsText, value);
        }

        public string PositionXText
        {
            get => _positionXText;
            private set => SetProperty(ref _positionXText, value);
        }

        public string PositionYText
        {
            get => _positionYText;
            private set => SetProperty(ref _positionYText, value);
        }

        public string PositionZText
        {
            get => _positionZText;
            private set => SetProperty(ref _positionZText, value);
        }

        public string PlayerCandidateDetailsText
        {
            get => _playerCandidateDetailsText;
            private set => SetProperty(ref _playerCandidateDetailsText, value);
        }

        public string AmmoCandidateDetailsText
        {
            get => _ammoCandidateDetailsText;
            private set => SetProperty(ref _ammoCandidateDetailsText, value);
        }

        public string CounterCandidateDetailsText
        {
            get => _counterCandidateDetailsText;
            private set => SetProperty(ref _counterCandidateDetailsText, value);
        }

        public string AddressCandidateDetailsText
        {
            get => _addressCandidateDetailsText;
            private set => SetProperty(ref _addressCandidateDetailsText, value);
        }

        public async Task RefreshAsync(CancellationToken cancellationToken)
        {
            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                DetectedGame? detectedGame = _detectedGame;
                if (_gameSession.UsesPollingProcessDetection)
                {
                    detectedGame = await Task.Run(
                        _gameSession.DetectByPolling,
                        cancellationToken);
                    await RunOnDispatcherAsync(
                        () => ApplyPolledDetectedGame(detectedGame),
                        cancellationToken);
                }

                if (_isDisconnecting)
                {
                    bool isDisconnectComplete = await Task.Run(
                        () => IsMonitorDisconnectComplete(DateTimeOffset.UtcNow),
                        cancellationToken);
                    await RunOnDispatcherAsync(
                        () =>
                        {
                            DetectedGame? currentGame = _detectedGame;
                            if (isDisconnectComplete)
                            {
                                CompleteMonitorDisconnect(currentGame);
                            }
                            else
                            {
                                ApplyDisconnectingState(currentGame);
                            }
                        },
                        cancellationToken);
                    return;
                }

                (
                    PlayerStatsReadResult readResult,
                    GameEventMonitorStatus eventStatus,
                    DllInjectionResult injectionResult) = await Task.Run(
                    () =>
                    {
                        DateTimeOffset receivedAt = DateTimeOffset.UtcNow;
                        StatsRefreshSnapshot snapshot = _statsRefreshService.Read(detectedGame, receivedAt);
                        ApplyMonitorReadinessTimeout(
                            snapshot.ReadResult.DetectedGame,
                            snapshot.EventStatus,
                            receivedAt);
                        return (snapshot.ReadResult, snapshot.EventStatus, _lastInjectionResult);
                    },
                    cancellationToken);
                await RunOnDispatcherAsync(
                    () =>
                    {
                        ApplyReadResult(readResult);
                        ApplyEventMonitorStatus(readResult.DetectedGame, injectionResult, eventStatus);
                        UpdateConnectButtonState(readResult.DetectedGame);
                    },
                    cancellationToken);
            }
            catch (InvalidOperationException ex) when (!cancellationToken.IsCancellationRequested)
            {
                await TryApplyReadErrorAsync(ex.Message, cancellationToken);
            }
            catch (Win32Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                await TryApplyReadErrorAsync(ex.Message, cancellationToken);
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                DetectedGame? detectedGame = _detectedGame;
                if (detectedGame is null || !CanAttemptConnect(detectedGame))
                {
                    await RunOnDispatcherAsync(
                        () =>
                        {
                            ApplyConnectionStatus(detectedGame);
                            UpdateConnectButtonState(detectedGame);
                        },
                        cancellationToken);
                    return;
                }

                _isConnecting = true;
                await RunOnDispatcherAsync(
                    () =>
                    {
                        ApplyConnectionStatus(detectedGame);
                        InjectionStatusText = AppStrings.Get("DllInjectionConnecting");
                        EventMonitorStatusText = AppStrings.Get("EventMonitorWaitingForConnect");
                        UpdateConnectButtonState(detectedGame);
                    },
                    cancellationToken);

                DllInjectionResult injectionResult = await Task.Run(
                    () => _gameSession.Inject(detectedGame),
                    cancellationToken);
                if (!Equals(_detectedGame, detectedGame))
                {
                    _isConnecting = false;
                    await RunOnDispatcherAsync(
                        () =>
                        {
                            ApplyConnectionStatus(_detectedGame);
                            ApplyEventMonitorStatus(_detectedGame, _lastInjectionResult, GameEventMonitorStatus.WaitingForMonitor);
                            UpdateConnectButtonState(_detectedGame);
                        },
                        cancellationToken);
                    return;
                }

                GameEventMonitorStatus eventStatus = GameEventMonitorStatus.WaitingForMonitor;
                DateTimeOffset? attemptedAt = null;
                if (IsMonitorLoadedInjectionState(injectionResult.State))
                {
                    attemptedAt = DateTimeOffset.UtcNow;
                    eventStatus = _gameSession.ReadEventMonitorStatus(DateTimeOffset.UtcNow, detectedGame.ProcessId);
                }

                _lastInjectionProcessId = detectedGame.ProcessId;
                _lastInjectionResult = injectionResult;
                _lastInjectionAttemptedAt = attemptedAt;
                _isConnecting = false;

                await RunOnDispatcherAsync(
                    () =>
                    {
                        ApplyConnectionStatus(detectedGame);
                        ApplyEventMonitorStatus(detectedGame, injectionResult, eventStatus);
                        UpdateConnectButtonState(detectedGame);
                    },
                    cancellationToken);
            }
            catch (InvalidOperationException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _isConnecting = false;
                await TryApplyReadErrorAsync(ex.Message, cancellationToken);
            }
            catch (Win32Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _isConnecting = false;
                await TryApplyReadErrorAsync(ex.Message, cancellationToken);
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                DetectedGame? detectedGame = _detectedGame;
                if (_isDisconnecting)
                {
                    await RunOnDispatcherAsync(() => ApplyDisconnectingState(detectedGame), cancellationToken);
                    return;
                }

                int? monitorProcessId = _lastInjectionProcessId;
                if (monitorProcessId is null || !IsMonitorLoadedInjectionState(_lastInjectionResult.State))
                {
                    await RunOnDispatcherAsync(
                        () =>
                        {
                            ResetMonitorConnectionState();
                            ApplyConnectionStatus(detectedGame);
                            ApplyEventMonitorStatus(detectedGame, _lastInjectionResult, GameEventMonitorStatus.WaitingForMonitor);
                            UpdateConnectButtonState(detectedGame);
                        },
                        cancellationToken);
                    return;
                }

                _isConnecting = false;
                _isDisconnecting = true;
                _disconnectProcessId = monitorProcessId;
                _disconnectRequestedAt = DateTimeOffset.UtcNow;
                _gameSession.RequestMonitorStop(monitorProcessId);
                await RunOnDispatcherAsync(
                    () => ApplyDisconnectingState(detectedGame),
                    cancellationToken);
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        public Task TryApplyRefreshErrorAsync(string message, CancellationToken cancellationToken)
        {
            return TryApplyReadErrorAsync(message, cancellationToken);
        }

        public void Dispose()
        {
            _disposed = true;
            _gameSession.DetectedGameChanged -= OnDetectedGameChanged;
            _gameSession.Dispose();
            _operationSemaphore.Dispose();
        }

        private static string FormatLine(string labelResourceId, string value)
        {
            return AppStrings.Format("LabeledValueFormat", AppStrings.Get(labelResourceId), value);
        }

        private static async Task RunOnDispatcherAsync(DispatcherQueue dispatcherQueue, Action action, CancellationToken cancellationToken)
        {
            if (dispatcherQueue.HasThreadAccess)
            {
                cancellationToken.ThrowIfCancellationRequested();
                action();
                return;
            }

            TaskCompletionSource completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(
                () => completionSource.TrySetCanceled(cancellationToken));

            bool queued = dispatcherQueue.TryEnqueue(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completionSource.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    action();
                    completionSource.TrySetResult();
                }
                catch (Exception ex)
                {
                    completionSource.TrySetException(ex);
                }
            });

            if (!queued)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                throw new InvalidOperationException(AppStrings.Get("DispatcherQueueFailed"));
            }

            await completionSource.Task;
        }

        private Task RunOnDispatcherAsync(Action action, CancellationToken cancellationToken)
        {
            return RunOnDispatcherAsync(_dispatcherQueue, action, cancellationToken);
        }

        private async Task TryApplyReadErrorAsync(string message, CancellationToken cancellationToken)
        {
            try
            {
                await RunOnDispatcherAsync(() => ApplyReadError(message), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (InvalidOperationException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private void ApplyReadResult(PlayerStatsReadResult result)
        {
            DetectedGameText = result.DetectedGame?.DisplayName ?? AppStrings.Get("NoGameDetected");
            ApplyConnectionStatus(result.DetectedGame, result.StatusText);

            if (result.Stats is null)
            {
                ClearStats();
                return;
            }

            PointsText = _formatter.FormatStat(result.Stats.Points);
            KillsText = _formatter.FormatStat(result.Stats.Kills);
            DownsText = _formatter.FormatStat(result.Stats.Downs);
            RevivesText = _formatter.FormatStat(result.Stats.Revives);
            HeadshotsText = _formatter.FormatStat(result.Stats.Headshots);
            PositionXText = _formatter.FormatCandidate(result.Stats.Candidates.PositionX);
            PositionYText = _formatter.FormatCandidate(result.Stats.Candidates.PositionY);
            PositionZText = _formatter.FormatCandidate(result.Stats.Candidates.PositionZ);
            PlayerCandidateDetailsText = FormatPlayerCandidateDetails(result.Stats.Candidates);
            AmmoCandidateDetailsText = FormatAmmoCandidateDetails(result.Stats.Candidates);
            CounterCandidateDetailsText = FormatCounterCandidateDetails(result.Stats.Candidates);
            AddressCandidateDetailsText = result.DetectedGame?.AddressMap is PlayerStatAddressMap addressMap
                ? FormatAddressCandidateDetails(addressMap)
                : EmptyStatText;
        }

        private void ApplyConnectionStatus(DetectedGame? detectedGame, string? connectedStatusText = null)
        {
            if (detectedGame is null)
            {
                StatusText = AppStrings.Get("GameNotRunning");
                SetConnectionState(detectedGame, ConnectionState.Disconnected);
                return;
            }

            if (!detectedGame.IsStatsSupported)
            {
                StatusText = FormatUnsupportedStatus(detectedGame);
                SetConnectionState(detectedGame, ConnectionState.Unsupported);
                return;
            }

            if (_isDisconnecting)
            {
                StatusText = AppStrings.Get("ConnectionStatusDisconnecting");
                SetConnectionState(detectedGame, ConnectionState.Disconnecting);
                return;
            }

            if (_isConnecting)
            {
                StatusText = AppStrings.Get("ConnectionStatusConnecting");
                SetConnectionState(detectedGame, ConnectionState.Detected);
                return;
            }

            if (IsMonitorConnectedFor(detectedGame))
            {
                StatusText = connectedStatusText ?? AppStrings.Format("ConnectedStatusFormat", detectedGame.DisplayName);
                SetConnectionState(detectedGame, ConnectionState.Connected);
                return;
            }

            StatusText = AppStrings.Format("GameDetectedConnectPromptFormat", detectedGame.DisplayName);
            SetConnectionState(detectedGame, ConnectionState.Detected);
        }

        private static string FormatUnsupportedStatus(DetectedGame detectedGame)
        {
            return string.IsNullOrWhiteSpace(detectedGame.UnsupportedReason)
                ? AppStrings.Format("UnsupportedStatusFormat", detectedGame.DisplayName)
                : AppStrings.Format("UnsupportedStatusWithReasonFormat", detectedGame.DisplayName, detectedGame.UnsupportedReason);
        }

        private string FormatPlayerCandidateDetails(PlayerCandidateStats candidates)
        {
            return string.Join(Environment.NewLine,
            [
                FormatLine("VelocityXLabel", _formatter.FormatCandidate(candidates.VelocityX)),
                FormatLine("VelocityYLabel", _formatter.FormatCandidate(candidates.VelocityY)),
                FormatLine("VelocityZLabel", _formatter.FormatCandidate(candidates.VelocityZ)),
                FormatLine("GravityFieldLabel", _formatter.FormatCandidate(candidates.Gravity)),
                FormatLine("SpeedFieldLabel", _formatter.FormatCandidate(candidates.Speed)),
                FormatLine("LastJumpHeightLabel", _formatter.FormatCandidate(candidates.LastJumpHeight)),
                FormatLine("AdsAmountLabel", _formatter.FormatCandidate(candidates.AdsAmount)),
                FormatLine("ViewAngleXLabel", _formatter.FormatCandidate(candidates.ViewAngleX)),
                FormatLine("ViewAngleYLabel", _formatter.FormatCandidate(candidates.ViewAngleY)),
                FormatLine("HeightIntLabel", _formatter.FormatCandidate(candidates.HeightInt)),
                FormatLine("HeightFloatLabel", _formatter.FormatCandidate(candidates.HeightFloat)),
                FormatLine("LegacyHealthLabel", _formatter.FormatCandidate(candidates.LegacyHealth)),
                FormatLine("PlayerInfoHealthLabel", _formatter.FormatCandidate(candidates.PlayerInfoHealth)),
                FormatLine("GEntityPlayerHealthLabel", _formatter.FormatCandidate(candidates.GEntityPlayerHealth))
            ]);
        }

        private string FormatAmmoCandidateDetails(PlayerCandidateStats candidates)
        {
            return string.Join(Environment.NewLine,
            [
                FormatLine("AmmoSlot0Label", _formatter.FormatCandidate(candidates.AmmoSlot0)),
                FormatLine("AmmoSlot1Label", _formatter.FormatCandidate(candidates.AmmoSlot1)),
                FormatLine("LethalAmmoLabel", _formatter.FormatCandidate(candidates.LethalAmmo)),
                FormatLine("AmmoSlot2Label", _formatter.FormatCandidate(candidates.AmmoSlot2)),
                FormatLine("TacticalAmmoLabel", _formatter.FormatCandidate(candidates.TacticalAmmo)),
                FormatLine("AmmoSlot3Label", _formatter.FormatCandidate(candidates.AmmoSlot3)),
                FormatLine("AmmoSlot4Label", _formatter.FormatCandidate(candidates.AmmoSlot4))
            ]);
        }

        private string FormatCounterCandidateDetails(PlayerCandidateStats candidates)
        {
            return string.Join(Environment.NewLine,
            [
                FormatLine("RoundCandidateLabel", _formatter.FormatCandidate(candidates.Round)),
                FormatLine("AlternateKillsLabel", _formatter.FormatCandidate(candidates.AlternateKills)),
                FormatLine("AlternateHeadshotsLabel", _formatter.FormatCandidate(candidates.AlternateHeadshots)),
                FormatLine("SecondaryKillsLabel", _formatter.FormatCandidate(candidates.SecondaryKills)),
                FormatLine("SecondaryHeadshotsLabel", _formatter.FormatCandidate(candidates.SecondaryHeadshots))
            ]);
        }

        private static string FormatAddressCandidateDetails(PlayerStatAddressMap addressMap)
        {
            DerivedPlayerStateAddresses derivedPlayerState = addressMap.DerivedPlayerState;
            PlayerCandidateAddresses candidates = addressMap.Candidates;
            return string.Join(Environment.NewLine,
            [
                FormatLine("LocalPlayerBaseLabel", StatFormatter.FormatAddress(derivedPlayerState.LocalPlayerBaseAddress)),
                FormatLine("GEntityArrayLabel", StatFormatter.FormatAddress(candidates.GEntityArrayAddress)),
                FormatLine("Zombie0GEntityLabel", StatFormatter.FormatAddress(candidates.Zombie0GEntityAddress)),
                FormatLine("GEntitySizeLabel", StatFormatter.FormatAddress(candidates.GEntitySize))
            ]);
        }

        private static string FormatEventCompatibility(GameCompatibilityState compatibilityState)
        {
            return compatibilityState switch
            {
                GameCompatibilityState.WaitingForMonitor => AppStrings.Get("EventMonitorWaitingForMonitor"),
                GameCompatibilityState.Compatible => AppStrings.Get("EventMonitorCompatible"),
                GameCompatibilityState.UnsupportedVersion => AppStrings.Get("EventMonitorUnsupportedVersion"),
                GameCompatibilityState.CaptureDisabled => AppStrings.Get("EventMonitorCaptureDisabled"),
                GameCompatibilityState.PollingFallback => AppStrings.Get("EventMonitorPollingFallback"),
                _ => AppStrings.Get("EventMonitorUnknown")
            };
        }

        private static string FormatRoundSession(GameEventMonitorStatus eventStatus)
        {
            GameEvent? sessionEvent = eventStatus.RecentEvents
                .LastOrDefault(gameEvent => gameEvent.EventType is GameEventType.StartOfRound or GameEventType.EndOfRound or GameEventType.EndGame);
            if (sessionEvent is null)
            {
                return EmptyStatText;
            }

            if (sessionEvent.EventType == GameEventType.EndGame)
            {
                return AppStrings.Get("RoundSessionEnded");
            }

            if (sessionEvent.LevelTime <= 0)
            {
                return EmptyStatText;
            }

            return AppStrings.Format("CurrentRoundFormat", sessionEvent.LevelTime, sessionEvent.EventName);
        }

        private bool IsMonitorDisconnectComplete(DateTimeOffset now)
        {
            if (!_isDisconnecting)
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

        private void CompleteMonitorDisconnect(DetectedGame? detectedGame)
        {
            ResetMonitorConnectionState(requestStop: false);
            ApplyConnectionStatus(detectedGame);
            ApplyEventMonitorStatus(detectedGame, _lastInjectionResult, GameEventMonitorStatus.WaitingForMonitor);
            UpdateConnectButtonState(detectedGame);
        }

        private void ApplyDisconnectingState(DetectedGame? detectedGame)
        {
            StatusText = AppStrings.Get("ConnectionStatusDisconnecting");
            InjectionStatusText = AppStrings.Get("DllInjectionDisconnecting");
            EventMonitorStatusText = AppStrings.Get("EventMonitorDisconnecting");
            LatestEventStatus = GameEventMonitorStatus.WaitingForMonitor;
            ConnectionLastUpdateText = EmptyStatText;
            CurrentRoundText = EmptyStatText;
            BoxEventsText = AppStrings.Get("RecentEventsEmpty");
            RecentGameEventsText = AppStrings.Get("RecentEventsEmpty");
            SetConnectionState(detectedGame, ConnectionState.Disconnecting);
            UpdateConnectButtonState(detectedGame);
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

        private bool CanAttemptConnect(DetectedGame? detectedGame)
        {
            return !_isConnecting
                && !_isDisconnecting
                && detectedGame is not null
                && detectedGame.Variant == GameVariant.SteamZombies
                && detectedGame.IsStatsSupported
                && !IsMonitorConnectedFor(detectedGame);
        }

        private bool HasInjectionAttemptFor(DetectedGame? detectedGame)
        {
            return detectedGame is not null
                && _lastInjectionProcessId == detectedGame.ProcessId
                && _lastInjectionResult.State != DllInjectionState.NotAttempted;
        }

        private bool IsMonitorConnectedFor(DetectedGame? detectedGame)
        {
            return detectedGame is not null
                && _lastInjectionProcessId == detectedGame.ProcessId
                && IsMonitorLoadedInjectionState(_lastInjectionResult.State);
        }

        private static bool IsMonitorLoadedInjectionState(DllInjectionState state)
        {
            return state is DllInjectionState.Loaded or DllInjectionState.AlreadyInjected;
        }

        private static string FormatInjectionStatus(
            DllInjectionResult injectionResult,
            GameEventMonitorStatus eventStatus)
        {
            if (injectionResult.State is not (DllInjectionState.Loaded or DllInjectionState.AlreadyInjected))
            {
                return injectionResult.Message;
            }

            return eventStatus.CompatibilityState switch
            {
                GameCompatibilityState.Compatible => AppStrings.Get("DllInjectionMonitorReady"),
                GameCompatibilityState.PollingFallback => AppStrings.Get("DllInjectionPollingFallback"),
                GameCompatibilityState.UnsupportedVersion => AppStrings.Get("DllInjectionUnsupportedVersion"),
                GameCompatibilityState.CaptureDisabled => AppStrings.Get("DllInjectionCaptureDisabled"),
                GameCompatibilityState.WaitingForMonitor => AppStrings.Get("DllInjectionWaitingForReadiness"),
                _ => injectionResult.Message
            };
        }

        private void ApplyEventMonitorStatus(
            DetectedGame? detectedGame,
            DllInjectionResult injectionResult,
            GameEventMonitorStatus eventStatus)
        {
            LatestEventStatus = eventStatus;

            if (_isDisconnecting)
            {
                ApplyDisconnectingState(detectedGame);
                return;
            }

            if (detectedGame is null)
            {
                InjectionStatusText = AppStrings.Get("DllInjectionNotAttempted");
                EventCompatibilityText = AppStrings.Get("NoGameDetected");
                EventMonitorStatusText = AppStrings.Get("EventMonitorWaitingForMonitor");
                ConnectionLastUpdateText = EmptyStatText;
                CurrentRoundText = EmptyStatText;
                BoxEventsText = AppStrings.Get("RecentEventsEmpty");
                RecentGameEventsText = AppStrings.Get("RecentEventsEmpty");
                return;
            }

            if (detectedGame.Variant != GameVariant.SteamZombies || detectedGame.AddressMap is null)
            {
                InjectionStatusText = AppStrings.Format(
                    "DllInjectionUnsupportedGameFormat",
                    detectedGame.DisplayName);
                EventCompatibilityText = AppStrings.Format(
                    "EventMonitorUnsupportedGameFormat",
                    detectedGame.DisplayName);
                EventMonitorStatusText = AppStrings.Get("EventMonitorCaptureDisabled");
                ConnectionLastUpdateText = EmptyStatText;
                CurrentRoundText = EmptyStatText;
                BoxEventsText = AppStrings.Get("RecentEventsEmpty");
                RecentGameEventsText = AppStrings.Get("RecentEventsEmpty");
                return;
            }

            EventCompatibilityText = AppStrings.Get("GameProcessDetectorDisplayNameSteamZombies");
            if (!IsMonitorConnectedFor(detectedGame))
            {
                InjectionStatusText = _isConnecting
                    ? AppStrings.Get("DllInjectionConnecting")
                    : HasInjectionAttemptFor(detectedGame)
                        ? injectionResult.Message
                        : AppStrings.Get("DllInjectionWaitingForConnect");
                EventMonitorStatusText = AppStrings.Get("EventMonitorWaitingForConnect");
                ConnectionLastUpdateText = EmptyStatText;
                CurrentRoundText = EmptyStatText;
                BoxEventsText = AppStrings.Get("RecentEventsEmpty");
                RecentGameEventsText = AppStrings.Get("RecentEventsEmpty");
                return;
            }

            InjectionStatusText = FormatInjectionStatus(injectionResult, eventStatus);
            ConnectionLastUpdateText = AppStrings.Get("ConnectionLastUpdateJustNow");
            string monitorStatusText = FormatEventCompatibility(eventStatus.CompatibilityState);
            if (eventStatus.DroppedEventCount > 0 || eventStatus.DroppedNotifyCount > 0)
            {
                monitorStatusText = AppStrings.Format(
                    "EventMonitorCaptureDropsFormat",
                    monitorStatusText,
                    eventStatus.DroppedEventCount,
                    eventStatus.DroppedNotifyCount,
                    eventStatus.PublishedNotifyCount);
            }
            else if (eventStatus.PublishedNotifyCount > 0)
            {
                monitorStatusText = AppStrings.Format(
                    "EventMonitorPublishedEventsFormat",
                    monitorStatusText,
                    eventStatus.PublishedNotifyCount);
            }

            EventMonitorStatusText = monitorStatusText;
            CurrentRoundText = FormatRoundSession(eventStatus);
            BoxEventsText = GameEventFormatter.FormatRecentBoxEvents(eventStatus);
            RecentGameEventsText = GameEventFormatter.FormatRecentGameEvents(eventStatus);
        }

        private void OnDetectedGameChanged(object? sender, DetectedGameChangedEventArgs args)
        {
            if (_disposed)
            {
                return;
            }

            if (_dispatcherQueue.HasThreadAccess)
            {
                ApplyDetectedGameChanged(args.DetectedGame);
                return;
            }

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (!_disposed)
                {
                    ApplyDetectedGameChanged(args.DetectedGame);
                }
            });
        }

        private void ApplyDetectedGameChanged(DetectedGame? detectedGame)
        {
            ApplyDetectedGameState(detectedGame);
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyPolledDetectedGame(DetectedGame? detectedGame)
        {
            if (Equals(_detectedGame, detectedGame))
            {
                return;
            }

            ApplyDetectedGameState(detectedGame);
        }

        private void ApplyDetectedGameState(DetectedGame? detectedGame)
        {
            _detectedGame = detectedGame;
            ResetMonitorConnectionState();
            DetectedGameText = detectedGame?.DisplayName ?? AppStrings.Get("NoGameDetected");
            ApplyConnectionStatus(detectedGame);
            ApplyEventMonitorStatus(detectedGame, _lastInjectionResult, GameEventMonitorStatus.WaitingForMonitor);
            UpdateConnectButtonState(detectedGame);
            ClearStats();
        }

        private void ResetMonitorConnectionState(bool requestStop = true)
        {
            int? monitorProcessId = _lastInjectionProcessId;
            _isConnecting = false;
            _isDisconnecting = false;
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

        private void UpdateConnectButtonState(DetectedGame? detectedGame)
        {
            ConnectButtonText = GetConnectButtonText(detectedGame);
            IsConnectButtonEnabled = CanAttemptConnect(detectedGame);
        }

        private string GetConnectButtonText(DetectedGame? detectedGame)
        {
            if (detectedGame is null)
            {
                return AppStrings.Get("ConnectButtonWaitingForGameText");
            }

            if (_isConnecting)
            {
                return AppStrings.Get("ConnectButtonConnectingText");
            }

            if (_isDisconnecting)
            {
                return AppStrings.Get("ConnectionCardStatusDisconnecting");
            }

            if (IsMonitorConnectedFor(detectedGame))
            {
                return AppStrings.Get("ConnectButtonConnectedText");
            }

            if (detectedGame.Variant != GameVariant.SteamZombies || !detectedGame.IsStatsSupported)
            {
                return AppStrings.Get("ConnectButtonUnsupportedText");
            }

            return AppStrings.Get("ConnectButtonText");
        }

        private void ApplyReadError(string message)
        {
            _isConnecting = false;
            _isDisconnecting = false;
            _disconnectProcessId = null;
            _disconnectRequestedAt = null;
            ClearStats();
            EventCompatibilityText = AppStrings.Get("NoGameDetected");
            InjectionStatusText = AppStrings.Get("DllInjectionNotAttempted");
            EventMonitorStatusText = AppStrings.Get("EventMonitorWaitingForMonitor");
            LatestEventStatus = GameEventMonitorStatus.WaitingForMonitor;
            ConnectionLastUpdateText = EmptyStatText;
            CurrentRoundText = EmptyStatText;
            BoxEventsText = AppStrings.Get("RecentEventsEmpty");
            RecentGameEventsText = AppStrings.Get("RecentEventsEmpty");
            StatusText = message;
            SetConnectionState(_detectedGame, ConnectionState.Disconnected);
            UpdateConnectButtonState(_detectedGame);
        }

        private void SetConnectionState(DetectedGame? detectedGame, ConnectionState connectionState)
        {
            UpdateGameFooterState(detectedGame);
            UpdateEventFooterState(detectedGame, connectionState);
            UpdateFooterIndicator(connectionState);
            UpdateConnectionCardState(connectionState);
        }

        private void UpdateGameFooterState(DetectedGame? detectedGame)
        {
            if (detectedGame is null)
            {
                GameStatusText = AppStrings.Get("FooterGameNotRunning");
                return;
            }

            GameStatusText = AppStrings.Format("FooterGameDetectedFormat", detectedGame.DisplayName);
        }

        private void UpdateEventFooterState(DetectedGame? detectedGame, ConnectionState connectionState)
        {
            if (connectionState == ConnectionState.Connected)
            {
                EventConnectionStatusText = AppStrings.Get("FooterEventsConnected");
                return;
            }

            if (connectionState == ConnectionState.Disconnecting || _isDisconnecting)
            {
                EventConnectionStatusText = AppStrings.Get("FooterEventsDisconnecting");
                return;
            }

            if (_isConnecting)
            {
                EventConnectionStatusText = AppStrings.Get("FooterEventsConnecting");
                return;
            }

            if (detectedGame is not null && !detectedGame.IsStatsSupported)
            {
                EventConnectionStatusText = AppStrings.Get("FooterEventsUnsupported");
                return;
            }

            EventConnectionStatusText = AppStrings.Get("FooterEventsNotConnected");
        }

        private void UpdateFooterIndicator(ConnectionState connectionState)
        {
            FooterSuccessStatusVisibility = connectionState == ConnectionState.Connected ? Visibility.Visible : Visibility.Collapsed;
            FooterPendingStatusVisibility = connectionState is ConnectionState.Detected or ConnectionState.Disconnecting or ConnectionState.Unsupported
                ? Visibility.Visible
                : Visibility.Collapsed;
            FooterDisconnectedStatusVisibility = connectionState == ConnectionState.Disconnected ? Visibility.Visible : Visibility.Collapsed;
            FooterErrorStatusVisibility = Visibility.Collapsed;
        }

        private void UpdateConnectionCardState(ConnectionState connectionState)
        {
            ConnectionCardStatusText = connectionState switch
            {
                ConnectionState.Connected => AppStrings.Get("ConnectionCardStatusConnected"),
                ConnectionState.Disconnecting => AppStrings.Get("ConnectionCardStatusDisconnecting"),
                ConnectionState.Unsupported => AppStrings.Get("ConnectionCardStatusUnsupported"),
                ConnectionState.Detected when _isConnecting => AppStrings.Get("ConnectionCardStatusConnecting"),
                ConnectionState.Detected => AppStrings.Get("ConnectionCardStatusMonitoring"),
                _ => AppStrings.Get("ConnectionCardStatusDisconnected")
            };

            ConnectButtonVisibility = connectionState is ConnectionState.Connected or ConnectionState.Disconnecting
                ? Visibility.Collapsed
                : Visibility.Visible;
            DisconnectButtonVisibility = connectionState == ConnectionState.Connected ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ClearStats()
        {
            PointsText = EmptyStatText;
            KillsText = EmptyStatText;
            DownsText = EmptyStatText;
            RevivesText = EmptyStatText;
            HeadshotsText = EmptyStatText;
            PositionXText = EmptyStatText;
            PositionYText = EmptyStatText;
            PositionZText = EmptyStatText;
            PlayerCandidateDetailsText = EmptyStatText;
            AmmoCandidateDetailsText = EmptyStatText;
            CounterCandidateDetailsText = EmptyStatText;
            AddressCandidateDetailsText = EmptyStatText;
        }

        private void SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value))
            {
                return;
            }

            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
