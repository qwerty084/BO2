using BO2.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace BO2.ViewModels
{
    public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
    {
        private const string EmptyStatText = "--";

        private readonly DispatcherQueue _dispatcherQueue;
        private readonly GameConnectionSession _connectionSession;
        private readonly GameConnectionSessionDisplayProjector _displayProjector;
        private readonly SemaphoreSlim _operationSemaphore = new(1, 1);
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
        private GameEventMonitorStatus _latestEventStatus = GameEventMonitorStatus.WaitingForMonitor;

        public MainWindowViewModel(DispatcherQueue dispatcherQueue)
            : this(dispatcherQueue, new GameConnectionSession())
        {
        }

        internal MainWindowViewModel(DispatcherQueue dispatcherQueue, GameConnectionSession connectionSession)
        {
            ArgumentNullException.ThrowIfNull(dispatcherQueue);
            ArgumentNullException.ThrowIfNull(connectionSession);

            _dispatcherQueue = dispatcherQueue;
            _connectionSession = connectionSession;
            _displayProjector = new GameConnectionSessionDisplayProjector(AppStrings.Get("UnavailableValue"));
            _connectionSession.DetectedGameChanged += OnDetectedGameChanged;

            _connectionSession.Start();
            ApplyRefreshSnapshot(_connectionSession.GetStatusSnapshot());
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
                GameConnectionRefreshResult snapshot = await Task.Run(
                    _connectionSession.Read,
                    cancellationToken);
                await RunOnDispatcherAsync(
                    () => ApplyRefreshSnapshot(snapshot),
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
                bool connectPending = false;
                try
                {
                    GameConnectionRefreshResult connectingSnapshot = await Task.Run(
                        _connectionSession.BeginConnect,
                        cancellationToken);
                    connectPending = connectingSnapshot.IsConnecting;
                    await RunOnDispatcherAsync(
                        () => ApplyRefreshSnapshot(connectingSnapshot),
                        cancellationToken);
                    if (!connectingSnapshot.IsConnecting)
                    {
                        return;
                    }

                    GameConnectionRefreshResult connectedSnapshot = await Task.Run(
                        _connectionSession.CompleteConnect,
                        cancellationToken);
                    connectPending = false;
                    await RunOnDispatcherAsync(
                        () => ApplyRefreshSnapshot(connectedSnapshot),
                        cancellationToken);
                }
                catch
                {
                    if (connectPending)
                    {
                        _connectionSession.CancelConnect();
                    }

                    throw;
                }
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

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                GameConnectionRefreshResult snapshot = await Task.Run(
                    _connectionSession.BeginDisconnect,
                    cancellationToken);
                await RunOnDispatcherAsync(
                    () => ApplyRefreshSnapshot(snapshot),
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
            _connectionSession.DetectedGameChanged -= OnDetectedGameChanged;
            _connectionSession.Dispose();
            _operationSemaphore.Dispose();
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
            GameConnectionRefreshResult snapshot = _connectionSession.HandleReadFailure();
            try
            {
                await RunOnDispatcherAsync(() => ApplyReadError(message, snapshot), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (InvalidOperationException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
        }

        private void ApplyRefreshSnapshot(GameConnectionRefreshResult snapshot)
        {
            ApplyDisplayState(_displayProjector.Project(snapshot));
        }

        private void ApplyDisplayState(GameConnectionSessionDisplayState state)
        {
            PointsText = state.PointsText;
            KillsText = state.KillsText;
            DownsText = state.DownsText;
            RevivesText = state.RevivesText;
            HeadshotsText = state.HeadshotsText;
            PositionXText = state.PositionXText;
            PositionYText = state.PositionYText;
            PositionZText = state.PositionZText;
            PlayerCandidateDetailsText = state.PlayerCandidateDetailsText;
            AmmoCandidateDetailsText = state.AmmoCandidateDetailsText;
            CounterCandidateDetailsText = state.CounterCandidateDetailsText;
            AddressCandidateDetailsText = state.AddressCandidateDetailsText;
            DetectedGameText = state.DetectedGameText;
            EventCompatibilityText = state.EventCompatibilityText;
            InjectionStatusText = state.InjectionStatusText;
            EventMonitorStatusText = state.EventMonitorStatusText;
            CurrentRoundText = state.CurrentRoundText;
            BoxEventsText = state.BoxEventsText;
            RecentGameEventsText = state.RecentGameEventsText;
            StatusText = state.StatusText;
            GameStatusText = state.GameStatusText;
            EventConnectionStatusText = state.EventConnectionStatusText;
            ConnectButtonText = state.ConnectButtonText;
            ConnectionCardStatusText = state.ConnectionCardStatusText;
            ConnectionLastUpdateText = state.ConnectionLastUpdateText;
            IsConnectButtonEnabled = state.IsConnectButtonEnabled;
            ConnectButtonVisibility = ToVisibility(state.IsConnectButtonVisible);
            DisconnectButtonVisibility = ToVisibility(state.IsDisconnectButtonVisible);
            FooterSuccessStatusVisibility = ToVisibility(state.IsFooterSuccessStatusVisible);
            FooterPendingStatusVisibility = ToVisibility(state.IsFooterPendingStatusVisible);
            FooterDisconnectedStatusVisibility = ToVisibility(state.IsFooterDisconnectedStatusVisible);
            FooterErrorStatusVisibility = ToVisibility(state.IsFooterErrorStatusVisible);
            LatestEventStatus = state.LatestEventStatus;
        }

        private void OnDetectedGameChanged(object? sender, DetectedGameChangedEventArgs args)
        {
            if (_disposed)
            {
                return;
            }

            if (_dispatcherQueue.HasThreadAccess)
            {
                ApplyDetectedGameChanged();
                return;
            }

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (!_disposed)
                {
                    ApplyDetectedGameChanged();
                }
            });
        }

        private void ApplyDetectedGameChanged()
        {
            ApplyRefreshSnapshot(_connectionSession.GetStatusSnapshot());
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyReadError(string message, GameConnectionRefreshResult snapshot)
        {
            ApplyRefreshSnapshot(snapshot);
            StatusText = message;
        }

        private static Visibility ToVisibility(bool isVisible)
        {
            return isVisible ? Visibility.Visible : Visibility.Collapsed;
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
