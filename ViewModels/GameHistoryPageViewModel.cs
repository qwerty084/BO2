using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using BO2.Services;

namespace BO2.ViewModels
{
    public sealed class GameHistoryPageViewModel : INotifyPropertyChanged
    {
        private readonly GameHistoryDisplayProjector _displayProjector = new();
        private bool _isHistoryRailOpen = true;
        private GameHistoryDetailViewModel? _selectedGame;
        private GameHistorySummaryViewModel? _selectedGameSummary;
        private string? _summaryLoadErrorText;
        private bool _isSelectedGameDetailLoading;
        private string? _selectedGameDetailErrorText;
        private string _recordingStatusTitle = AppStrings.Get("GameHistoryRecordingStatusWaitingTitle");
        private string _recordingStatusText = AppStrings.Get("GameHistoryRecordingStatusWaitingText");

        public event PropertyChangedEventHandler? PropertyChanged;

        public event EventHandler<GameHistoryDetailRequestedEventArgs>? SelectedGameDetailRequested;

        public ObservableCollection<GameHistorySummaryViewModel> SavedGames { get; } = [];

        public GameHistoryDetailViewModel? SelectedGame
        {
            get => _selectedGame;
            private set
            {
                if (Equals(_selectedGame, value))
                {
                    return;
                }

                _selectedGame = value;
                OnSelectedGameStateChanged();
            }
        }

        public GameHistorySummaryViewModel? SelectedGameSummary
        {
            get => _selectedGameSummary;
            set => SetSelectedGameSummary(value, requestDetail: value is not null);
        }

        public string RecordingStatusTitle
        {
            get => _recordingStatusTitle;
            private set => SetProperty(ref _recordingStatusTitle, value);
        }

        public string RecordingStatusText
        {
            get => _recordingStatusText;
            private set => SetProperty(ref _recordingStatusText, value);
        }

        public bool HasSavedGames => SavedGames.Count > 0;

        public bool IsSavedGamesListVisible => HasSavedGames && !IsSummaryLoadErrorVisible;

        public bool IsEmptyVisible => IsListVisible && !HasSavedGames && !IsSummaryLoadErrorVisible;

        public bool IsListVisible => SelectedGameSummary is null;

        public bool IsDetailVisible => SelectedGameSummary is not null;

        public bool IsSummaryLoadErrorVisible => IsListVisible && !string.IsNullOrWhiteSpace(_summaryLoadErrorText);

        public bool IsHistoryRailVisible => IsDetailVisible && _isHistoryRailOpen;

        public bool IsHistoryRailReopenButtonVisible => IsDetailVisible && !_isHistoryRailOpen;

        public bool IsSelectedGameDetailLoadingVisible => IsDetailVisible && _isSelectedGameDetailLoading;

        public bool IsSelectedGameDetailContentVisible =>
            IsDetailVisible
            && !_isSelectedGameDetailLoading
            && SelectedGame is not null
            && !IsSelectedGameDetailErrorVisible;

        public bool IsSelectedGameDetailErrorVisible =>
            IsDetailVisible
            && !_isSelectedGameDetailLoading
            && !string.IsNullOrWhiteSpace(_selectedGameDetailErrorText);

        public string TrackedGameCountText => AppStrings.Format("GameHistoryTrackedGameCountFormat", SavedGames.Count);

        public string EmptyStateTitle => AppStrings.Get("GameHistoryEmptyTitle");

        public string EmptyStateText => AppStrings.Get("GameHistoryEmptyText");

        public string SummaryLoadErrorTitle => AppStrings.Get("GameHistoryLoadErrorTitle");

        public string SummaryLoadErrorText => _summaryLoadErrorText ?? string.Empty;

        public string SelectedGameDetailLoadingTitle => AppStrings.Get("GameHistoryDetailLoadingTitle");

        public string SelectedGameDetailLoadingText => AppStrings.Get("GameHistoryDetailLoadingText");

        public string SelectedGameDetailErrorTitle => AppStrings.Get("GameHistoryDetailLoadErrorTitle");

        public string SelectedGameDetailErrorText => _selectedGameDetailErrorText ?? string.Empty;

        internal void ReplaceSummaries(IEnumerable<GameHistorySummary> summaries)
        {
            ArgumentNullException.ThrowIfNull(summaries);

            ReplaceSavedGameSummaries(_displayProjector.ProjectSavedSummaries(summaries));
        }

        internal void ReplaceSavedGames(IEnumerable<GameHistoryEntry> savedGames)
        {
            ArgumentNullException.ThrowIfNull(savedGames);

            ReplaceSavedGameSummaries(_displayProjector.ProjectSavedGameSummaries(savedGames));
        }

        private void ReplaceSavedGameSummaries(IEnumerable<GameHistorySummaryDisplayState> summaries)
        {
            ArgumentNullException.ThrowIfNull(summaries);

            ClearSummaryLoadError();

            string? selectedId = SelectedGameSummary?.Id ?? SelectedGame?.Id;

            GameHistorySummaryViewModel[] summaryViewModels = [.. summaries.Select(CreateSummaryViewModel)];

            SavedGames.Clear();
            foreach (GameHistorySummaryViewModel summary in summaryViewModels)
            {
                SavedGames.Add(summary);
            }

            OnPropertyChanged(nameof(HasSavedGames));
            OnPropertyChanged(nameof(IsSavedGamesListVisible));
            OnPropertyChanged(nameof(IsEmptyVisible));
            OnPropertyChanged(nameof(TrackedGameCountText));

            GameHistorySummaryViewModel? summaryToSelect = null;
            if (selectedId is not null)
            {
                summaryToSelect = SavedGames.FirstOrDefault(
                    game => string.Equals(game.Id, selectedId, StringComparison.Ordinal));
            }

            SetSelectedGameSummary(summaryToSelect, requestDetail: false);
        }

        internal void ShowSummaryLoadError(string message)
        {
            SetSelectedGameSummary(null, requestDetail: false);
            SavedGames.Clear();
            _summaryLoadErrorText = string.IsNullOrWhiteSpace(message)
                ? AppStrings.Get("GameHistoryLoadErrorText")
                : message;

            OnPropertyChanged(nameof(HasSavedGames));
            OnPropertyChanged(nameof(IsSavedGamesListVisible));
            OnPropertyChanged(nameof(IsEmptyVisible));
            OnPropertyChanged(nameof(IsSummaryLoadErrorVisible));
            OnPropertyChanged(nameof(SummaryLoadErrorText));
            OnPropertyChanged(nameof(TrackedGameCountText));
        }

        public void SelectGame(GameHistorySummaryViewModel summary)
        {
            ArgumentNullException.ThrowIfNull(summary);

            SetHistoryRailOpen(true);
            SetSelectedGameSummary(summary, requestDetail: true);
        }

        public void SelectGameById(string id)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);

            GameHistorySummaryViewModel? summary = SavedGames.FirstOrDefault(
                game => string.Equals(game.Id, id, StringComparison.Ordinal));
            SetHistoryRailOpen(true);
            SetSelectedGameSummary(summary, requestDetail: summary is not null);
        }

        public void ShowList()
        {
            SetSelectedGameSummary(null, requestDetail: false);
        }

        internal void ShowSelectedGameDetail(GameHistoryEntry game)
        {
            ArgumentNullException.ThrowIfNull(game);

            if (SelectedGameSummary is null
                || !string.Equals(SelectedGameSummary.Id, game.Id, StringComparison.Ordinal))
            {
                return;
            }

            _isSelectedGameDetailLoading = false;
            _selectedGameDetailErrorText = null;
            SelectedGame = CreateDetail(game);
        }

        internal void ShowSelectedGameDetailError(string message)
        {
            _selectedGame = null;
            _isSelectedGameDetailLoading = false;
            _selectedGameDetailErrorText = string.IsNullOrWhiteSpace(message)
                ? AppStrings.Get("GameHistoryDetailLoadErrorText")
                : message;
            OnSelectedGameStateChanged();
        }

        public void HideHistoryRail()
        {
            SetHistoryRailOpen(false);
        }

        public void ShowHistoryRail()
        {
            SetHistoryRailOpen(true);
        }

        internal void ApplyRecordingStatus(GameHistoryRecordingStatus status)
        {
            ArgumentNullException.ThrowIfNull(status);

            ApplyRecordingStatusDisplayState(_displayProjector.ProjectRecordingStatus(status));
        }

        internal void ApplySnapshot(GameConnectionSnapshot snapshot)
        {
            if (snapshot.ConnectionPhase != GameConnectionPhase.Connected)
            {
                ApplyRecordingStatus(GameHistoryRecordingStatus.Unavailable(
                    GameHistoryRecordingUnavailableReason.NotConnected));
                return;
            }

            GameConnectionEventMonitorSummary eventMonitor = snapshot.EventMonitorSummary;
            if (HasDroppedEventMonitorData(eventMonitor.Status))
            {
                ApplyRecordingStatus(GameHistoryRecordingStatus.Discarded(
                    GameHistoryRecordingDiscardReason.DroppedLifecycleData));
                return;
            }

            if (eventMonitor.State != GameConnectionEventMonitorState.Ready)
            {
                ApplyRecordingStatus(GameHistoryRecordingStatus.Unavailable(
                    GameHistoryRecordingUnavailableReason.RequiresHookBackedEventMonitor));
                return;
            }

            if (snapshot.MapIdentityResult?.IsSupportedMap == true
                && snapshot.MapIdentityResult.Identity is GameMapIdentity identity
                && !string.IsNullOrWhiteSpace(identity.DisplayName))
            {
                ApplyRecordingStatus(GameHistoryRecordingStatus.WaitingForRoundOne(identity.DisplayName));
                return;
            }

            ApplyRecordingStatus(GameHistoryRecordingStatus.Unavailable(
                ProjectUnavailableReason(snapshot.MapIdentityResult)));
        }

        private static GameHistoryRecordingUnavailableReason ProjectUnavailableReason(
            GameMapIdentityReadResult? mapIdentityResult)
        {
            return mapIdentityResult?.Status switch
            {
                GameMapIdentityReadStatus.SupportedMap => GameHistoryRecordingUnavailableReason.MissingFriendlyMapName,
                GameMapIdentityReadStatus.UnsupportedMapIdentity
                    or GameMapIdentityReadStatus.UnsupportedVariant => GameHistoryRecordingUnavailableReason.RequiresSupportedMap,
                _ => GameHistoryRecordingUnavailableReason.MissingMapIdentity
            };
        }

        private void ApplyRecordingStatusDisplayState(GameHistoryRecordingStatusDisplayState state)
        {
            ArgumentNullException.ThrowIfNull(state);

            RecordingStatusTitle = state.Title;
            RecordingStatusText = state.BodyText;
        }

        private static GameHistorySummaryViewModel CreateSummaryViewModel(GameHistorySummaryDisplayState state)
        {
            return new GameHistorySummaryViewModel(state);
        }

        private GameHistoryDetailViewModel CreateDetail(GameHistoryEntry game)
        {
            return new GameHistoryDetailViewModel(_displayProjector.ProjectSelectedDetail(game));
        }

        private static bool HasDroppedEventMonitorData(GameEventMonitorStatus status)
        {
            return status.DroppedEventCount > 0 || status.DroppedNotifyCount > 0;
        }

        private void SetProperty<T>(
            ref T storage,
            T value,
            [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value))
            {
                return;
            }

            storage = value;
            OnPropertyChanged(propertyName);
        }

        private void SetHistoryRailOpen(bool isOpen)
        {
            if (_isHistoryRailOpen == isOpen)
            {
                return;
            }

            _isHistoryRailOpen = isOpen;
            OnPropertyChanged(nameof(IsHistoryRailVisible));
            OnPropertyChanged(nameof(IsHistoryRailReopenButtonVisible));
        }

        private void SetSelectedGameSummary(GameHistorySummaryViewModel? summary, bool requestDetail)
        {
            if (ReferenceEquals(_selectedGameSummary, summary))
            {
                if (requestDetail && summary is not null && !_isSelectedGameDetailLoading)
                {
                    BeginSelectedGameDetailLoad();
                    SelectedGameDetailRequested?.Invoke(
                        this,
                        new GameHistoryDetailRequestedEventArgs(summary.Id));
                }

                return;
            }

            if (_selectedGameSummary is not null)
            {
                _selectedGameSummary.IsSelected = false;
            }

            _selectedGameSummary = summary;
            if (_selectedGameSummary is not null)
            {
                _selectedGameSummary.IsSelected = true;
            }

            OnPropertyChanged(nameof(SelectedGameSummary));
            if (_selectedGameSummary is null)
            {
                ClearSelectedGameDetail();
                return;
            }

            if (requestDetail)
            {
                BeginSelectedGameDetailLoad();
                SelectedGameDetailRequested?.Invoke(
                    this,
                    new GameHistoryDetailRequestedEventArgs(_selectedGameSummary.Id));
                return;
            }

            OnSelectedGameStateChanged();
        }

        private void BeginSelectedGameDetailLoad()
        {
            _selectedGame = null;
            _selectedGameDetailErrorText = null;
            _isSelectedGameDetailLoading = true;
            OnSelectedGameStateChanged();
        }

        private void ClearSelectedGameDetail()
        {
            _selectedGame = null;
            _selectedGameDetailErrorText = null;
            _isSelectedGameDetailLoading = false;
            OnSelectedGameStateChanged();
        }

        private void ClearSummaryLoadError()
        {
            if (_summaryLoadErrorText is null)
            {
                return;
            }

            _summaryLoadErrorText = null;
            OnPropertyChanged(nameof(IsSavedGamesListVisible));
            OnPropertyChanged(nameof(IsEmptyVisible));
            OnPropertyChanged(nameof(IsSummaryLoadErrorVisible));
            OnPropertyChanged(nameof(SummaryLoadErrorText));
        }

        private void OnSelectedGameStateChanged()
        {
            OnPropertyChanged(nameof(SelectedGame));
            OnPropertyChanged(nameof(IsListVisible));
            OnPropertyChanged(nameof(IsDetailVisible));
            OnPropertyChanged(nameof(IsSavedGamesListVisible));
            OnPropertyChanged(nameof(IsEmptyVisible));
            OnPropertyChanged(nameof(IsSummaryLoadErrorVisible));
            OnPropertyChanged(nameof(IsHistoryRailVisible));
            OnPropertyChanged(nameof(IsHistoryRailReopenButtonVisible));
            OnPropertyChanged(nameof(IsSelectedGameDetailLoadingVisible));
            OnPropertyChanged(nameof(IsSelectedGameDetailContentVisible));
            OnPropertyChanged(nameof(IsSelectedGameDetailErrorVisible));
            OnPropertyChanged(nameof(SelectedGameDetailErrorText));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class GameHistorySummaryViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        internal GameHistorySummaryViewModel(GameHistorySummaryDisplayState displayState)
        {
            ArgumentNullException.ThrowIfNull(displayState);

            DisplayState = displayState;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id => DisplayState.Id;

        public string DateText => DisplayState.DateText;

        public string MapNameText => DisplayState.MapNameText;

        public string FinalRoundText => DisplayState.FinalRoundText;

        public string GameDurationText => DisplayState.GameDurationText;

        public string PointsText => DisplayState.PointsText;

        public string KillsText => DisplayState.KillsText;

        public string DownsText => DisplayState.DownsText;

        public string RevivesText => DisplayState.RevivesText;

        public string HeadshotsText => DisplayState.HeadshotsText;

        public bool IsSelected
        {
            get => _isSelected;
            internal set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        internal GameHistorySummaryDisplayState DisplayState { get; }
    }

    public sealed class GameHistoryDetailRequestedEventArgs(string id) : EventArgs
    {
        public string Id { get; } = id;
    }

    public sealed class GameHistoryDetailViewModel
    {
        internal GameHistoryDetailViewModel(GameHistoryDetailDisplayState displayState)
        {
            ArgumentNullException.ThrowIfNull(displayState);

            DisplayState = displayState;
            FinalStats = new GameHistoryStatsDisplayViewModel(displayState.FinalStats);
            Rounds = new ReadOnlyObservableCollection<GameHistoryRoundViewModel>(
                new ObservableCollection<GameHistoryRoundViewModel>(
                    displayState.Rounds.Select(static round => new GameHistoryRoundViewModel(round))));
            BoxEvents = new ReadOnlyObservableCollection<GameHistoryBoxEventViewModel>(
                new ObservableCollection<GameHistoryBoxEventViewModel>(
                    displayState.BoxEvents.Select(static boxEvent => new GameHistoryBoxEventViewModel(boxEvent))));
            BoxWeaponAverages = new ReadOnlyObservableCollection<GameHistoryBoxWeaponAverageViewModel>(
                new ObservableCollection<GameHistoryBoxWeaponAverageViewModel>(
                    displayState.BoxWeaponAverages.Select(static average => new GameHistoryBoxWeaponAverageViewModel(average))));
            BoxRollCountText = displayState.BoxRollCountText;
            BoxUniqueWeaponCountText = displayState.BoxUniqueWeaponCountText;
            BoxAverageRollsPerRoundText = displayState.BoxAverageRollsPerRoundText;
            BoxMostSeenWeaponText = displayState.BoxMostSeenWeaponText;
        }

        public string Id => DisplayState.Id;

        public string DateText => DisplayState.DateText;

        public string MapNameText => DisplayState.MapNameText;

        public string FinalRoundText => DisplayState.FinalRoundText;

        public string GameDurationText => DisplayState.GameDurationText;

        public GameHistoryStatsDisplayViewModel FinalStats { get; }

        public ReadOnlyObservableCollection<GameHistoryRoundViewModel> Rounds { get; }

        public ReadOnlyObservableCollection<GameHistoryBoxEventViewModel> BoxEvents { get; }

        public ReadOnlyObservableCollection<GameHistoryBoxWeaponAverageViewModel> BoxWeaponAverages { get; }

        public string BoxRollCountText { get; }

        public string BoxUniqueWeaponCountText { get; }

        public string BoxAverageRollsPerRoundText { get; }

        public string BoxMostSeenWeaponText { get; }

        public bool HasBoxEvents => BoxEvents.Count > 0;

        public bool IsBoxEventsEmpty => !HasBoxEvents;

        public string BoxEventsEmptyText => AppStrings.Get("GameHistoryBoxEventsEmpty");

        internal GameHistoryDetailDisplayState DisplayState { get; }
    }

    public sealed class GameHistoryRoundViewModel
    {
        internal GameHistoryRoundViewModel(GameHistoryRoundDisplayState displayState)
        {
            ArgumentNullException.ThrowIfNull(displayState);

            DisplayState = displayState;
            CumulativeStats = new GameHistoryStatsDisplayViewModel(displayState.CumulativeStats);
            DeltaStats = new GameHistoryStatsDisplayViewModel(displayState.DeltaStats);
        }

        public int RoundNumber => DisplayState.RoundNumber;

        public string RoundTitleText => DisplayState.RoundTitleText;

        public string DurationText => DisplayState.DurationText;

        public GameHistoryStatsDisplayViewModel CumulativeStats { get; }

        public GameHistoryStatsDisplayViewModel DeltaStats { get; }

        internal GameHistoryRoundDisplayState DisplayState { get; }
    }

    public sealed class GameHistoryStatsDisplayViewModel
    {
        internal GameHistoryStatsDisplayViewModel(GameHistoryStatsDisplayState displayState)
        {
            ArgumentNullException.ThrowIfNull(displayState);

            DisplayState = displayState;
        }

        public string PointsText => DisplayState.PointsText;

        public string KillsText => DisplayState.KillsText;

        public string DownsText => DisplayState.DownsText;

        public string RevivesText => DisplayState.RevivesText;

        public string HeadshotsText => DisplayState.HeadshotsText;

        internal GameHistoryStatsDisplayState DisplayState { get; }
    }

    public sealed class GameHistoryBoxEventViewModel
    {
        internal GameHistoryBoxEventViewModel(GameHistoryBoxEventDisplayState displayState)
        {
            ArgumentNullException.ThrowIfNull(displayState);

            DisplayState = displayState;
        }

        public string ReceivedAtText => DisplayState.ReceivedAtText;

        public string RoundText => DisplayState.RoundText;

        public string WeaponText => DisplayState.WeaponText;

        public string PrimaryText => DisplayState.PrimaryText;

        internal GameHistoryBoxEventDisplayState DisplayState { get; }
    }

    public sealed class GameHistoryBoxWeaponAverageViewModel
    {
        internal GameHistoryBoxWeaponAverageViewModel(GameHistoryBoxWeaponAverageDisplayState displayState)
        {
            ArgumentNullException.ThrowIfNull(displayState);

            DisplayState = displayState;
        }

        public string WeaponText => DisplayState.WeaponText;

        public string RollCountText => DisplayState.RollCountText;

        public string AverageRoundText => DisplayState.AverageRoundText;

        public string ShareText => DisplayState.ShareText;

        internal GameHistoryBoxWeaponAverageDisplayState DisplayState { get; }
    }
}
