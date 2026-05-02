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
        private readonly GameConnectionSessionDisplayRenderer _displayRenderer;
        private readonly HomeStatsViewModel _homeStats = new();
        private readonly SemaphoreSlim _operationSemaphore = new(1, 1);
        private bool _disposed;
        private string _detectedGameText = AppStrings.Get("NoGameDetected");
        private string _eventMonitorStatusText = AppStrings.Get("EventMonitorWaitingForMonitor");
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
            _displayProjector = new GameConnectionSessionDisplayProjector();
            _displayRenderer = new GameConnectionSessionDisplayRenderer();
            _connectionSession.SnapshotChanged += OnSnapshotChanged;

            _connectionSession.Start();
            ApplyRefreshSnapshot(_connectionSession.GetStatusSnapshot());
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public event EventHandler<GameEventMonitorStatus>? EventStatusUpdated;

        public event EventHandler? RefreshRequested;

        public HomeStatsViewModel HomeStats => _homeStats;

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

        public async Task RefreshAsync(CancellationToken cancellationToken)
        {
            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                await Task.Run(
                    _connectionSession.Read,
                    cancellationToken);
            }
            catch (InvalidOperationException ex) when (!cancellationToken.IsCancellationRequested)
            {
                await TryApplyReadErrorAsync(ex.Message, sessionAlreadyHandled: true, cancellationToken);
            }
            catch (Win32Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                await TryApplyReadErrorAsync(ex.Message, sessionAlreadyHandled: true, cancellationToken);
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
                await Task.Run(
                    _connectionSession.Connect,
                    cancellationToken);
            }
            catch (InvalidOperationException ex) when (!cancellationToken.IsCancellationRequested)
            {
                await TryApplyReadErrorAsync(ex.Message, sessionAlreadyHandled: true, cancellationToken);
            }
            catch (Win32Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                await TryApplyReadErrorAsync(ex.Message, sessionAlreadyHandled: true, cancellationToken);
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
                await Task.Run(
                    _connectionSession.Disconnect,
                    cancellationToken);
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        public Task TryApplyRefreshErrorAsync(string message, CancellationToken cancellationToken)
        {
            return TryApplyReadErrorAsync(message, sessionAlreadyHandled: false, cancellationToken);
        }

        public void Dispose()
        {
            _disposed = true;
            _connectionSession.SnapshotChanged -= OnSnapshotChanged;
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

        private async Task TryApplyReadErrorAsync(
            string message,
            bool sessionAlreadyHandled,
            CancellationToken cancellationToken)
        {
            GameConnectionSnapshot snapshot = sessionAlreadyHandled
                ? _connectionSession.Snapshot
                : _connectionSession.HandleReadFailure();
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

        private void ApplyRefreshSnapshot(GameConnectionSnapshot snapshot)
        {
            _homeStats.ApplySnapshot(snapshot);
            ApplyShellDisplayState(_displayRenderer.Render(_displayProjector.Project(snapshot)));
        }

        private void ApplyShellDisplayState(GameConnectionSessionDisplayState state)
        {
            DetectedGameText = state.DetectedGameText;
            EventMonitorStatusText = state.EventMonitorStatusText;
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

        private void OnSnapshotChanged(object? sender, GameConnectionSnapshotChangedEventArgs args)
        {
            if (_disposed)
            {
                return;
            }

            if (_dispatcherQueue.HasThreadAccess)
            {
                ApplySnapshotChanged(args);
                return;
            }

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (!_disposed)
                {
                    ApplySnapshotChanged(args);
                }
            });
        }

        private void ApplySnapshotChanged(GameConnectionSnapshotChangedEventArgs args)
        {
            ApplyRefreshSnapshot(args.Snapshot);
            if (!Equals(args.PreviousSnapshot.CurrentGame, args.Snapshot.CurrentGame))
            {
                RefreshRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ApplyReadError(string message, GameConnectionSnapshot snapshot)
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
