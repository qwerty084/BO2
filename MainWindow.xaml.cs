using BO2.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace BO2
{
    public sealed partial class MainWindow : Window
    {
        private readonly DispatcherTimer _refreshTimer;

        public MainWindow()
        {
            ViewModel = new MainWindowViewModel();
            InitializeComponent();

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _refreshTimer.Tick += OnRefreshTimerTick;
            _refreshTimer.Start();
            Closed += OnClosed;
            RootNavigationView.SelectedItem = HomeNavigationItem;
            ShowHome();
            ViewModel.Refresh();
        }

        public MainWindowViewModel ViewModel { get; }

        private void OnRefreshTimerTick(object? sender, object e)
        {
            ViewModel.Refresh();
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            _refreshTimer.Stop();
            _refreshTimer.Tick -= OnRefreshTimerTick;
            ViewModel.Dispose();
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
            PageTitle.Text = "Home";
            HomeContent.Visibility = Visibility.Visible;
            SettingsContent.Visibility = Visibility.Collapsed;
        }

        private void ShowSettings()
        {
            PageTitle.Text = "Settings";
            HomeContent.Visibility = Visibility.Collapsed;
            SettingsContent.Visibility = Visibility.Visible;
        }
    }
}
