using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using BO2.Services;

namespace BO2.ViewModels
{
    public sealed class HomeStatsViewModel : INotifyPropertyChanged
    {
        private const string EmptyStatText = "--";

        private readonly CurrentGamePageDisplayProjector _currentGamePageDisplayProjector = new();
        private readonly GameConnectionSessionDisplayProjector _displayProjector = new();
        private readonly GameConnectionSessionDisplayRenderer _displayRenderer = new();
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

        public event PropertyChangedEventHandler? PropertyChanged;

        public string PointsText
        {
            get => _pointsText;
            private set => SetProperty(ref _pointsText, value);
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

        public string EventMonitorStatusText
        {
            get => _eventMonitorStatusText;
            private set => SetProperty(ref _eventMonitorStatusText, value);
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

        public string RecentGameEventsText
        {
            get => _recentGameEventsText;
            private set => SetProperty(ref _recentGameEventsText, value);
        }

        internal void ApplySnapshot(GameConnectionSnapshot snapshot)
        {
            CurrentGamePageDisplayState currentGameState = _currentGamePageDisplayProjector.Project(snapshot);
            GameConnectionSessionDisplayState sessionState = _displayRenderer.Render(_displayProjector.Project(snapshot));
            ApplyDisplayState(currentGameState, sessionState);
        }

        private void ApplyDisplayState(
            CurrentGamePageDisplayState currentGameState,
            GameConnectionSessionDisplayState sessionState)
        {
            ArgumentNullException.ThrowIfNull(currentGameState);
            ArgumentNullException.ThrowIfNull(sessionState);

            PointsText = currentGameState.PointsText;
            KillsText = currentGameState.KillsText;
            DownsText = currentGameState.DownsText;
            RevivesText = currentGameState.RevivesText;
            HeadshotsText = currentGameState.HeadshotsText;
            // Candidate/address fields remain on Home Stats until the Current Game Page replacement removes them.
            PositionXText = sessionState.PositionXText;
            PositionYText = sessionState.PositionYText;
            PositionZText = sessionState.PositionZText;
            PlayerCandidateDetailsText = sessionState.PlayerCandidateDetailsText;
            AmmoCandidateDetailsText = sessionState.AmmoCandidateDetailsText;
            CounterCandidateDetailsText = sessionState.CounterCandidateDetailsText;
            AddressCandidateDetailsText = sessionState.AddressCandidateDetailsText;
            DetectedGameText = currentGameState.DetectedGameText;
            EventCompatibilityText = currentGameState.EventCompatibilityText;
            InjectionStatusText = currentGameState.InjectionStatusText;
            EventMonitorStatusText = currentGameState.EventMonitorStatusText;
            CurrentRoundText = currentGameState.CurrentRoundText;
            BoxEventsText = currentGameState.BoxEventsText;
            RecentGameEventsText = currentGameState.RecentGameEventsText;
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
