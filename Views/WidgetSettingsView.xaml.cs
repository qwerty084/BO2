using System;
using BO2.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BO2.Views
{
    public sealed partial class WidgetSettingsView : UserControl
    {
        private bool _isUpdatingWidgetControls;

        public WidgetSettingsView()
        {
            InitializeComponent();
        }

        public event EventHandler<BoxTrackerEnabledChangedEventArgs>? BoxTrackerEnabledChanged;

        public event EventHandler? ConfigureSelectedWidgetRequested;

        public void SetBoxTrackerEnabled(bool isEnabled)
        {
            _isUpdatingWidgetControls = true;
            try
            {
                BoxTrackerWidgetCheckBox.IsChecked = isEnabled;
            }
            finally
            {
                _isUpdatingWidgetControls = false;
            }
        }

        public void ShowSettingsRecovery(WidgetSettingsLoadRecovery? recovery)
        {
            if (recovery is null)
            {
                WidgetSettingsRecoveryInfoBar.IsOpen = false;
                return;
            }

            WidgetSettingsRecoveryInfoBar.Title = AppStrings.Get("WidgetSettingsRecoveryTitle");
            WidgetSettingsRecoveryInfoBar.Message = string.IsNullOrWhiteSpace(recovery.BackupPath)
                ? AppStrings.Get("WidgetSettingsRecoveryNoBackupMessage")
                : AppStrings.Format("WidgetSettingsRecoveryMessageFormat", recovery.BackupPath);
            WidgetSettingsRecoveryInfoBar.IsOpen = true;
        }

        private void OnBoxTrackerWidgetCheckBoxChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingWidgetControls)
            {
                return;
            }

            BoxTrackerEnabledChanged?.Invoke(
                this,
                new BoxTrackerEnabledChangedEventArgs(BoxTrackerWidgetCheckBox.IsChecked.GetValueOrDefault()));
        }

        private void OnConfigureSelectedWidgetClick(object sender, RoutedEventArgs e)
        {
            ConfigureSelectedWidgetRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public sealed class BoxTrackerEnabledChangedEventArgs : EventArgs
    {
        public BoxTrackerEnabledChangedEventArgs(bool isEnabled)
        {
            IsEnabled = isEnabled;
        }

        public bool IsEnabled { get; }
    }
}
