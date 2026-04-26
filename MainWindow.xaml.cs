using BO2.Services;
using BO2.ViewModels;
using BO2.Widgets;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
        private readonly WidgetWindowManager _widgetWindowManager;
        private bool _isUpdatingWidgetControls;

        public MainWindow()
        {
            ViewModel = new MainWindowViewModel(DispatcherQueue);
            InitializeComponent();
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
            RootNavigationView.SelectedItem = HomeNavigationItem;
            ShowHome();
            RefreshWidgetControls();
            _widgetWindowManager.RestoreEnabledWidgets();
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
            _widgetWindowManager.SettingsChanged -= OnWidgetSettingsChanged;
            ViewModel.EventStatusUpdated -= OnEventStatusUpdated;
            _widgetWindowManager.Dispose();

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

        private void OnEventStatusUpdated(object? sender, GameEventMonitorStatus eventStatus)
        {
            _widgetWindowManager.UpdateEventStatus(eventStatus);
        }

        private void OnWidgetSettingsChanged(object? sender, EventArgs e)
        {
            RefreshWidgetControls();
        }

        private void OnBoxTrackerWidgetCheckBoxChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingWidgetControls)
            {
                return;
            }

            _widgetWindowManager.SetBoxTrackerEnabled(BoxTrackerWidgetCheckBox.IsChecked == true);
        }

        private async void OnConfigureSelectedWidgetClick(object sender, RoutedEventArgs e)
        {
            WidgetSettings settings = _widgetWindowManager.BoxTrackerSettings.Clone();

            NumberBox widthBox = CreateNumberBox(AppStrings.Get("WidgetWidthLabel"), settings.Width, 160, 3840);
            NumberBox heightBox = CreateNumberBox(AppStrings.Get("WidgetHeightLabel"), settings.Height, 80, 2160);
            ColorPicker backgroundColorPicker = CreateColorPicker(settings.BackgroundColor, Colors.White);
            ColorPicker textColorPicker = CreateColorPicker(settings.TextColor, Colors.Black);
            CheckBox transparentBackgroundCheckBox = new()
            {
                Content = AppStrings.Get("WidgetTransparentBackgroundLabel"),
                IsChecked = settings.TransparentBackground
            };
            CheckBox alwaysOnTopCheckBox = new()
            {
                Content = AppStrings.Get("WidgetAlwaysOnTopLabel"),
                IsChecked = settings.AlwaysOnTop
            };
            CheckBox centerAlignCheckBox = new()
            {
                Content = AppStrings.Get("WidgetCenterAlignLabel"),
                IsChecked = settings.CenterAlign
            };

            StackPanel content = new()
            {
                Spacing = 12,
                MaxWidth = 520
            };
            content.Children.Add(widthBox);
            content.Children.Add(heightBox);
            content.Children.Add(CreateLabeledColorPicker(AppStrings.Get("WidgetBackgroundColorLabel"), backgroundColorPicker));
            content.Children.Add(CreateLabeledColorPicker(AppStrings.Get("WidgetTextColorLabel"), textColorPicker));
            content.Children.Add(transparentBackgroundCheckBox);
            content.Children.Add(alwaysOnTopCheckBox);
            content.Children.Add(centerAlignCheckBox);

            ContentDialog dialog = new()
            {
                Title = AppStrings.Get("BoxTrackerWidgetConfigTitle"),
                Content = new ScrollViewer { Content = content },
                PrimaryButtonText = AppStrings.Get("WidgetConfigSaveButton"),
                CloseButtonText = AppStrings.Get("WidgetConfigCancelButton"),
                DefaultButton = ContentDialogButton.Primary
            };

            if (Content is FrameworkElement rootElement)
            {
                dialog.XamlRoot = rootElement.XamlRoot;
            }

            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            settings.Width = ReadNumberBoxValue(widthBox, WidgetSettings.DefaultWidth);
            settings.Height = ReadNumberBoxValue(heightBox, WidgetSettings.DefaultHeight);
            settings.BackgroundColor = WidgetColorSerializer.Format(backgroundColorPicker.Color);
            settings.TextColor = WidgetColorSerializer.Format(textColorPicker.Color);
            settings.TransparentBackground = transparentBackgroundCheckBox.IsChecked == true;
            settings.AlwaysOnTop = alwaysOnTopCheckBox.IsChecked == true;
            settings.CenterAlign = centerAlignCheckBox.IsChecked == true;
            _widgetWindowManager.ApplyBoxTrackerSettings(settings);
        }

        private void RefreshWidgetControls()
        {
            _isUpdatingWidgetControls = true;
            try
            {
                BoxTrackerWidgetCheckBox.IsChecked = _widgetWindowManager.IsBoxTrackerEnabled;
            }
            finally
            {
                _isUpdatingWidgetControls = false;
            }
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

        private static NumberBox CreateNumberBox(string header, int value, int minimum, int maximum)
        {
            return new NumberBox
            {
                Header = header,
                Value = value,
                Minimum = minimum,
                Maximum = maximum,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
            };
        }

        private static ColorPicker CreateColorPicker(string value, Windows.UI.Color defaultColor)
        {
            return new ColorPicker
            {
                Color = WidgetColorSerializer.ParseOrDefault(value, defaultColor),
                IsAlphaEnabled = true,
                IsColorSliderVisible = true,
                IsColorChannelTextInputVisible = true,
                IsHexInputVisible = true
            };
        }

        private static FrameworkElement CreateLabeledColorPicker(string label, ColorPicker colorPicker)
        {
            StackPanel panel = new()
            {
                Spacing = 6
            };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Colors.Gray)
            });
            panel.Children.Add(colorPicker);
            return panel;
        }

        private static int ReadNumberBoxValue(NumberBox numberBox, int fallback)
        {
            if (double.IsNaN(numberBox.Value))
            {
                return fallback;
            }

            return (int)Math.Round(numberBox.Value);
        }
    }
}
