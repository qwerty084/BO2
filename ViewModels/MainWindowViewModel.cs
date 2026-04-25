using BO2.Services;
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
        private string _statusText = "Waiting for t6zm.exe";

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
                PlayerStats stats = _memoryReader.ReadPlayerStats();
                PointsText = FormatStat(stats.Points);
                KillsText = FormatStat(stats.Kills);
                DownsText = FormatStat(stats.Downs);
                RevivesText = FormatStat(stats.Revives);
                HeadshotsText = FormatStat(stats.Headshots);
                StatusText = "Reading t6zm.exe player stat block at 0x0234C068";
            }
            catch (InvalidOperationException ex)
            {
                ClearStats();
                StatusText = ex.Message;
            }
            catch (Win32Exception ex)
            {
                ClearStats();
                StatusText = ex.Message;
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
