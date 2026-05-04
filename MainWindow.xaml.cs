using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BO2.Services;
using BO2.ViewModels;
using BO2.Views;
using BO2.Widgets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace BO2
{
    public sealed partial class MainWindow : Window, IDisposable
    {
        private readonly DispatcherTimer _refreshTimer;
        private readonly CancellationTokenSource _refreshCancellationTokenSource = new();
        private Task? _refreshTask;
        private bool _refreshPending;
        private int _cleanupRequested;
        private int _disposeRequested;
        private readonly WidgetWindowManager _widgetWindowManager;
        private readonly AppPreferencesStore _preferencesStore = AppPreferencesStore.CreateDefault();
        private readonly AppPreferences _preferences;
        private bool _isUpdatingThemeMode;
        private long _paneOpenChangedToken;
        private Storyboard? _settingsCogStoryboard;

        public MainWindow()
        {
            _preferences = _preferencesStore.Load();
            ViewModel = new MainWindowViewModel(DispatcherQueue);
            ViewModel.RefreshRequested += OnViewModelRefreshRequested;
            InitializeComponent();
            TryDisableWindowCornerRounding();
            ApplyThemeMode(_preferences.ThemeMode);
            _paneOpenChangedToken = RootNavigationView.RegisterPropertyChangedCallback(
                NavigationView.IsPaneOpenProperty,
                OnNavigationPaneOpenChanged);
            _widgetWindowManager = new WidgetWindowManager();
            _widgetWindowManager.SettingsChanged += OnWidgetSettingsChanged;
            ViewModel.EventStatusUpdated += OnEventStatusUpdated;

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _refreshTimer.Tick += OnRefreshTimerTick;
            _refreshTimer.Start();
            Closed += OnClosed;
            RootNavigationView.SelectedItem = CurrentGamePageNavigationItem;
            ShowCurrentGamePage();
            RefreshPaneFooterVisibility();
            RefreshThemeControls();
            RefreshWidgetSettingsRecoveryMessage();
            RefreshWidgetControls();
            _widgetWindowManager.RestoreEnabledWidgets();
            QueueRefresh();
        }

        public MainWindowViewModel ViewModel { get; }

        private void OnRefreshTimerTick(object? sender, object e)
        {
            QueueRefresh();
        }

        private void OnViewModelRefreshRequested(object? sender, EventArgs e)
        {
            QueueRefresh();
        }

        private async void OnConnectButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                await ViewModel.ConnectAsync(_refreshCancellationTokenSource.Token);
                QueueRefresh();
            }
            catch (OperationCanceledException) when (_refreshCancellationTokenSource.IsCancellationRequested)
            {
                return;
            }
        }

        private async void OnDisconnectButtonClick(object sender, RoutedEventArgs e)
        {
            try
            {
                await ViewModel.DisconnectAsync(_refreshCancellationTokenSource.Token);
                QueueRefresh();
            }
            catch (OperationCanceledException) when (_refreshCancellationTokenSource.IsCancellationRequested)
            {
                return;
            }
        }

        private void OnSettingsPaneItemTapped(object sender, TappedRoutedEventArgs e)
        {
            PlaySettingsCogAnimation();
            ShowSettings();
        }

        private void PlaySettingsCogAnimation()
        {
            _settingsCogStoryboard?.Stop();
            SettingsPaneIconRotation.Angle = 0;

            DoubleAnimation spinAnimation = new()
            {
                From = 0,
                To = 360,
                Duration = new Duration(TimeSpan.FromMilliseconds(260)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            Storyboard.SetTarget(spinAnimation, SettingsPaneIconRotation);
            Storyboard.SetTargetProperty(spinAnimation, "Angle");

            _settingsCogStoryboard = new Storyboard();
            _settingsCogStoryboard.Children.Add(spinAnimation);
            _settingsCogStoryboard.Begin();
        }

        private void OnNavigationPaneOpenChanged(DependencyObject sender, DependencyProperty dp)
        {
            RefreshPaneFooterVisibility();
        }

        private void RefreshPaneFooterVisibility()
        {
            bool isPaneOpen = RootNavigationView.IsPaneOpen;
            ConnectionPaneFooter.Visibility = isPaneOpen ? Visibility.Visible : Visibility.Collapsed;
            PaneFooterHost.Padding = isPaneOpen
                ? new Thickness(18, 0, 18, 16)
                : new Thickness(6, 0, 6, 16);
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            Dispose();
        }

        public void Dispose()
        {
            Closed -= OnClosed;
            if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
            {
                return;
            }

            if (_paneOpenChangedToken != 0)
            {
                RootNavigationView.UnregisterPropertyChangedCallback(
                    NavigationView.IsPaneOpenProperty,
                    _paneOpenChangedToken);
                _paneOpenChangedToken = 0;
            }

            _refreshTimer.Stop();
            _refreshTimer.Tick -= OnRefreshTimerTick;
            ViewModel.RefreshRequested -= OnViewModelRefreshRequested;
            _refreshCancellationTokenSource.Cancel();
            _widgetWindowManager.SettingsChanged -= OnWidgetSettingsChanged;
            ViewModel.EventStatusUpdated -= OnEventStatusUpdated;
            _widgetWindowManager.Dispose();
            GC.SuppressFinalize(this);

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

            if (ReferenceEquals(sender.SelectedItem, CurrentGamePageNavigationItem))
            {
                ShowCurrentGamePage();
                return;
            }

            if (ReferenceEquals(sender.SelectedItem, SettingsNavigationItem))
            {
                ShowSettings();
            }
        }

        private void ShowCurrentGamePage()
        {
            PageTitle.Text = AppStrings.Get("NavigationCurrentGame");
            CurrentGamePageContent.Visibility = Visibility.Visible;
            SettingsContent.Visibility = Visibility.Collapsed;

            if (!ReferenceEquals(RootNavigationView.SelectedItem, CurrentGamePageNavigationItem))
            {
                RootNavigationView.SelectedItem = CurrentGamePageNavigationItem;
            }
        }

        private void ShowSettings()
        {
            if (!ReferenceEquals(RootNavigationView.SelectedItem, SettingsNavigationItem))
            {
                RootNavigationView.SelectedItem = SettingsNavigationItem;
            }

            PageTitle.Text = AppStrings.Get("NavigationSettings");
            CurrentGamePageContent.Visibility = Visibility.Collapsed;
            SettingsContent.Visibility = Visibility.Visible;
        }

        private void OnEventStatusUpdated(object? sender, GameEventMonitorStatus eventStatus)
        {
            _widgetWindowManager.UpdateEventStatus(eventStatus);
        }

        private void OnWidgetSettingsChanged(object? sender, EventArgs e)
        {
            RefreshWidgetControls();
        }

        private void OnBoxTrackerEnabledChanged(object sender, BoxTrackerEnabledChangedEventArgs e)
        {
            _widgetWindowManager.SetBoxTrackerEnabled(e.IsEnabled);
        }

        private void OnThemeModeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingThemeMode)
            {
                return;
            }

            ThemeMode themeMode = ReadSelectedThemeMode();
            _preferences.ThemeMode = themeMode;
            _preferencesStore.Save(_preferences);
            ApplyThemeMode(themeMode);
        }

        private async void OnConfigureSelectedWidgetRequested(object? sender, EventArgs e)
        {
            if (Content is not FrameworkElement rootElement)
            {
                return;
            }

            WidgetSettings? settings = await BoxTrackerWidgetSettingsDialog.ShowAsync(
                rootElement.XamlRoot,
                _widgetWindowManager.BoxTrackerSettings,
                ResolveElementTheme(_preferences.ThemeMode));
            if (settings is null)
            {
                return;
            }

            _widgetWindowManager.ApplyBoxTrackerSettings(settings);
        }

        private void RefreshWidgetControls()
        {
            WidgetSettingsView.SetBoxTrackerEnabled(_widgetWindowManager.IsBoxTrackerEnabled);
        }

        private void RefreshWidgetSettingsRecoveryMessage()
        {
            WidgetSettingsView.ShowSettingsRecovery(_widgetWindowManager.SettingsLoadRecovery);
        }

        private void RefreshThemeControls()
        {
            _isUpdatingThemeMode = true;
            try
            {
                ThemeModeComboBox.SelectedIndex = _preferences.ThemeMode switch
                {
                    ThemeMode.Light => 1,
                    ThemeMode.Dark => 2,
                    _ => 0
                };
            }
            finally
            {
                _isUpdatingThemeMode = false;
            }
        }

        private ThemeMode ReadSelectedThemeMode()
        {
            if (ThemeModeComboBox.SelectedItem is ComboBoxItem item
                && item.Tag is string tag
                && Enum.TryParse(tag, out ThemeMode themeMode))
            {
                return themeMode;
            }

            return ThemeMode.System;
        }

        private void ApplyThemeMode(ThemeMode themeMode)
        {
            RootNavigationView.RequestedTheme = ResolveElementTheme(themeMode);
        }

        private static ElementTheme ResolveElementTheme(ThemeMode themeMode)
        {
            return themeMode switch
            {
                ThemeMode.Light => ElementTheme.Light,
                ThemeMode.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
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
                return;
            }
            catch (Exception ex) when (!_refreshCancellationTokenSource.IsCancellationRequested)
            {
                await ViewModel.TryApplyRefreshErrorAsync(ex.Message, _refreshCancellationTokenSource.Token);
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

        private void TryDisableWindowCornerRounding()
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                return;
            }

            try
            {
                nint hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                int cornerPreference = DwmWindowCornerPreferenceDoNotRound;

                // WinUI has no managed API for disabling rounded corners on this top-level HWND.
                // codeql[cs/call-to-unmanaged-code]
                _ = DwmSetWindowAttribute(
                    hWnd,
                    DwmWindowCornerPreferenceAttribute,
                    ref cornerPreference,
                    Marshal.SizeOf<int>());
            }
            catch (COMException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                return;
            }
        }

        private const uint DwmWindowCornerPreferenceAttribute = 33;
        private const int DwmWindowCornerPreferenceDoNotRound = 1;

        // Required to set Windows 11 DWM corner preference for the app-owned HWND.
        // codeql[cs/unmanaged-code]
        [DllImport("dwmapi.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int DwmSetWindowAttribute(
            nint hWnd,
            uint dwAttribute,
            ref int pvAttribute,
            int cbAttribute);
    }
}
