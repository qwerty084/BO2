using BO2.Services;
using Microsoft.UI.Xaml;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BO2.ViewModels
{
    public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly GameMemoryReader _memoryReader = new();
        private string _pointsText = "--";
        private string _killsText = "--";
        private string _downsText = "--";
        private string _revivesText = "--";
        private string _headshotsText = "--";
        private string _detectedGameText = "No game detected";
        private string _statusText = "Game not running";
        private Visibility _connectedStatusVisibility = Visibility.Collapsed;
        private Visibility _unsupportedStatusVisibility = Visibility.Collapsed;
        private Visibility _disconnectedStatusVisibility = Visibility.Visible;

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

        public void Refresh()
        {
            try
            {
                PlayerStatsReadResult result = _memoryReader.ReadPlayerStats();
                StatusText = result.StatusText;
                DetectedGameText = result.DetectedGame?.DisplayName ?? "No game detected";
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
            catch (InvalidOperationException ex)
            {
                ClearStats();
                StatusText = ex.Message;
                SetConnectionState(ConnectionState.Disconnected);
            }
            catch (Win32Exception ex)
            {
                ClearStats();
                StatusText = ex.Message;
                SetConnectionState(ConnectionState.Disconnected);
            }
        }

        public void Dispose()
        {
            _memoryReader.Dispose();
        }

        private static string FormatStat(int value)
        {
            return value.ToString("N0");
        }

        private void SetConnectionState(ConnectionState connectionState)
        {
            ConnectedStatusVisibility = connectionState == ConnectionState.Connected ? Visibility.Visible : Visibility.Collapsed;
            UnsupportedStatusVisibility = connectionState == ConnectionState.Unsupported ? Visibility.Visible : Visibility.Collapsed;
            DisconnectedStatusVisibility = connectionState == ConnectionState.Disconnected ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ClearStats()
        {
            PointsText = "--";
            KillsText = "--";
            DownsText = "--";
            RevivesText = "--";
            HeadshotsText = "--";
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
