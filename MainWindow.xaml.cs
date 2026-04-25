using BO2.Services;
using BO2.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BO2
{
    public sealed partial class MainWindow : Window
    {
        private readonly DispatcherTimer _refreshTimer;
        private readonly CancellationTokenSource _refreshCancellationTokenSource = new();
        private Task? _refreshTask;
        private bool _refreshPending;
        private int _cleanupRequested;

        public MainWindow()
        {
            ViewModel = new MainWindowViewModel(DispatcherQueue);
            InitializeComponent();

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _refreshTimer.Tick += OnRefreshTimerTick;
            _refreshTimer.Start();
            Closed += OnClosed;
            RootNavigationView.SelectedItem = HomeNavigationItem;
            ShowHome();
            QueueRefresh();
        }

        public MainWindowViewModel ViewModel { get; }

        private void OnRefreshTimerTick(object? sender, object e)
        {
            QueueRefresh();
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            Closed -= OnClosed;
            _refreshTimer.Stop();
            _refreshTimer.Tick -= OnRefreshTimerTick;
            _refreshCancellationTokenSource.Cancel();

            Task? refreshTask = _refreshTask;
            if (refreshTask is null || refreshTask.IsCompleted)
            {
                DisposeRefreshResources();
                return;
            }

            _ = refreshTask.ContinueWith(
                static (_, state) => ((MainWindow)state!).DisposeRefreshResources(),
                this,
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }

        private void OnNavigationViewSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                ShowSettings();
                return;
            }

            ShowHome();
        }

        private void ShowHome()
        {
            PageTitle.Text = AppStrings.Get("NavigationHome");
            HomeContent.Visibility = Visibility.Visible;
            SettingsContent.Visibility = Visibility.Collapsed;
        }

        private void ShowSettings()
        {
            PageTitle.Text = AppStrings.Get("NavigationSettings");
            HomeContent.Visibility = Visibility.Collapsed;
            SettingsContent.Visibility = Visibility.Visible;
        }

        private void QueueRefresh()
        {
            _refreshPending = true;
            if (_refreshTask is null || _refreshTask.IsCompleted)
            {
                _refreshTask = ProcessRefreshQueueAsync();
            }
        }

        private async Task ProcessRefreshQueueAsync()
        {
            try
            {
                while (_refreshPending && !_refreshCancellationTokenSource.IsCancellationRequested)
                {
                    _refreshPending = false;
                    await ViewModel.RefreshAsync(_refreshCancellationTokenSource.Token);
                }
            }
            catch (OperationCanceledException) when (_refreshCancellationTokenSource.IsCancellationRequested)
            {
            }
        }

        private void DisposeRefreshResources()
        {
            if (Interlocked.Exchange(ref _cleanupRequested, 1) != 0)
            {
                return;
            }

            _refreshCancellationTokenSource.Dispose();
            ViewModel.Dispose();
        }
    }
}
