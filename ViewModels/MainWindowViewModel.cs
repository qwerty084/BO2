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
        private readonly GameMemoryReader _memoryReader = new();
        private string _pointsText = EmptyStatText;
        private string _killsText = EmptyStatText;
        private string _downsText = EmptyStatText;
        private string _revivesText = EmptyStatText;
        private string _headshotsText = EmptyStatText;
        private string _detectedGameText = AppStrings.Get("NoGameDetected");
        private string _statusText = AppStrings.Get("GameNotRunning");
        private Visibility _connectedStatusVisibility = Visibility.Collapsed;
        private Visibility _unsupportedStatusVisibility = Visibility.Collapsed;
        private Visibility _disconnectedStatusVisibility = Visibility.Visible;

        public MainWindowViewModel(DispatcherQueue dispatcherQueue)
        {
            _dispatcherQueue = dispatcherQueue;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

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

        public string DetectedGameText
        {
            get => _detectedGameText;
            private set => SetProperty(ref _detectedGameText, value);
        }

        public Visibility ConnectedStatusVisibility
        {
            get => _connectedStatusVisibility;
            private set => SetProperty(ref _connectedStatusVisibility, value);
        }

        public Visibility UnsupportedStatusVisibility
        {
            get => _unsupportedStatusVisibility;
            private set => SetProperty(ref _unsupportedStatusVisibility, value);
        }

        public Visibility DisconnectedStatusVisibility
        {
            get => _disconnectedStatusVisibility;
            private set => SetProperty(ref _disconnectedStatusVisibility, value);
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

        public async Task RefreshAsync(CancellationToken cancellationToken)
        {
            try
            {
                PlayerStatsReadResult result = await Task.Run(_memoryReader.ReadPlayerStats, cancellationToken);
                await RunOnDispatcherAsync(() => ApplyReadResult(result), cancellationToken);
            }
            catch (InvalidOperationException ex) when (!cancellationToken.IsCancellationRequested)
            {
                await TryApplyReadErrorAsync(ex.Message, cancellationToken);
            }
            catch (Win32Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                await TryApplyReadErrorAsync(ex.Message, cancellationToken);
            }
        }

        public Task TryApplyRefreshErrorAsync(string message, CancellationToken cancellationToken)
        {
            return TryApplyReadErrorAsync(message, cancellationToken);
        }

        public void Dispose()
        {
            _memoryReader.Dispose();
        }

        private static string FormatStat(int value)
        {
            return value.ToString("N0");
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
            StatusText = result.StatusText;
            DetectedGameText = result.DetectedGame?.DisplayName ?? AppStrings.Get("NoGameDetected");
            SetConnectionState(result.ConnectionState);

            if (result.Stats is null)
            {
                ClearStats();
                return;
            }

            PointsText = FormatStat(result.Stats.Points);
            KillsText = FormatStat(result.Stats.Kills);
            DownsText = FormatStat(result.Stats.Downs);
            RevivesText = FormatStat(result.Stats.Revives);
            HeadshotsText = FormatStat(result.Stats.Headshots);
        }

        private void ApplyReadError(string message)
        {
            ClearStats();
            StatusText = message;
            SetConnectionState(ConnectionState.Disconnected);
        }

        private void SetConnectionState(ConnectionState connectionState)
        {
            ConnectedStatusVisibility = connectionState == ConnectionState.Connected ? Visibility.Visible : Visibility.Collapsed;
            UnsupportedStatusVisibility = connectionState == ConnectionState.Unsupported ? Visibility.Visible : Visibility.Collapsed;
            DisconnectedStatusVisibility = connectionState == ConnectionState.Disconnected ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ClearStats()
        {
            PointsText = EmptyStatText;
            KillsText = EmptyStatText;
            DownsText = EmptyStatText;
            RevivesText = EmptyStatText;
            HeadshotsText = EmptyStatText;
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
