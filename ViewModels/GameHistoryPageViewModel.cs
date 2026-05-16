using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using BO2.Services;

namespace BO2.ViewModels
{
    public sealed class GameHistoryPageViewModel : INotifyPropertyChanged
    {
        private const string CompletedBoxRollEventName = "randomization_done";

        public const string MissingValueText = "--";

        private readonly StatFormatter _statFormatter = new(MissingValueText);
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

            ReplaceSavedGames(summaries.Select(CreateEntry));
        }

        internal void ReplaceSavedGames(IEnumerable<GameHistoryEntry> savedGames)
        {
            ArgumentNullException.ThrowIfNull(savedGames);

            ClearSummaryLoadError();

            string? selectedId = SelectedGameSummary?.Id ?? SelectedGame?.Id;

            GameHistorySummaryViewModel[] summaries = [.. savedGames
                .OrderByDescending(static game => game.EndedAt)
                .ThenByDescending(static game => game.StartedAt)
                .ThenBy(static game => game.Id, StringComparer.Ordinal)
                .Select(CreateSummary)];

            SavedGames.Clear();
            foreach (GameHistorySummaryViewModel summary in summaries)
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

        private GameHistorySummaryViewModel CreateSummary(GameHistoryEntry game)
        {
            return new GameHistorySummaryViewModel(
                game,
                FormatDate(game.EndedAt),
                game.MapIdentity.FriendlyName,
                AppStrings.Format("GameHistoryFinalRoundFormat", game.FinalRound),
                FormatDuration(game.GameDuration),
                _statFormatter.FormatStat(game.FinalStats.Points),
                _statFormatter.FormatStat(game.FinalStats.Kills),
                _statFormatter.FormatStat(game.FinalStats.Downs),
                _statFormatter.FormatStat(game.FinalStats.Revives),
                _statFormatter.FormatStat(game.FinalStats.Headshots));
        }

        private static GameHistoryEntry CreateEntry(GameHistorySummary summary)
        {
            return new GameHistoryEntry
            {
                Id = summary.Id,
                StartedAt = summary.StartedAt,
                EndedAt = summary.EndedAt,
                MapIdentity = summary.MapIdentity,
                FinalRound = summary.FinalRound,
                FinalStats = new GameHistoryStats
                {
                    Points = summary.FinalStats.Points,
                    Kills = summary.FinalStats.Kills,
                    Downs = summary.FinalStats.Downs,
                    Revives = summary.FinalStats.Revives,
                    Headshots = summary.FinalStats.Headshots
                },
                GameDuration = summary.GameDuration
            };
        }

        private GameHistoryDetailViewModel CreateDetail(GameHistoryEntry game)
        {
            IReadOnlyList<GameHistoryBoxEvent> boxRolls =
            [
                .. game.BoxEvents
                    .Where(IsCompletedBoxRoll)
                    .OrderBy(static boxEvent => boxEvent.ReceivedAt)
            ];
            IReadOnlyList<GameHistoryBoxWeaponAverageViewModel> boxWeaponAverages = CreateBoxWeaponAverages(boxRolls);

            return new GameHistoryDetailViewModel(
                game.Id,
                FormatDate(game.EndedAt),
                game.MapIdentity.FriendlyName,
                AppStrings.Format("GameHistoryFinalRoundFormat", game.FinalRound),
                FormatDuration(game.GameDuration),
                CreateStatsDisplay(game.FinalStats, isDelta: false),
                [.. game.Rounds
                    .OrderBy(static round => round.RoundNumber)
                    .Select(CreateRound)],
                [.. boxRolls.Select(CreateBoxRoll)],
                boxWeaponAverages,
                FormatCount(boxRolls.Count),
                FormatCount(boxWeaponAverages.Count),
                FormatAverageRollsPerRound(boxRolls.Count, game.FinalRound),
                boxWeaponAverages.Count == 0 ? MissingValueText : boxWeaponAverages[0].WeaponText);
        }

        private GameHistoryRoundViewModel CreateRound(GameHistoryRound round)
        {
            return new GameHistoryRoundViewModel(
                round.RoundNumber,
                AppStrings.Format("GameHistoryRoundTitleFormat", round.RoundNumber),
                FormatDuration(round.RoundDuration),
                CreateStatsDisplay(round.CumulativeStats, isDelta: false),
                CreateStatsDisplay(round.DeltaStats, isDelta: true));
        }

        private GameHistoryBoxEventViewModel CreateBoxRoll(GameHistoryBoxEvent boxEvent)
        {
            string receivedAtText = FormatDate(boxEvent.ReceivedAt);
            string roundText = AppStrings.Format("GameHistoryRoundTitleFormat", boxEvent.RoundNumber);
            string weaponText = FormatBoxWeapon(boxEvent);
            string primaryText = AppStrings.Format(
                "GameHistoryBoxRollPrimaryFormat",
                roundText,
                weaponText);

            return new GameHistoryBoxEventViewModel(
                receivedAtText,
                roundText,
                weaponText,
                primaryText);
        }

        private IReadOnlyList<GameHistoryBoxWeaponAverageViewModel> CreateBoxWeaponAverages(
            IReadOnlyList<GameHistoryBoxEvent> boxRolls)
        {
            int totalRolls = boxRolls.Count;
            if (totalRolls == 0)
            {
                return [];
            }

            return
            [
                .. boxRolls
                    .GroupBy(FormatBoxWeapon, StringComparer.CurrentCultureIgnoreCase)
                    .Select(group => new
                    {
                        WeaponText = group.Key,
                        RollCount = group.Count(),
                        AverageRound = group.Average(static boxEvent => boxEvent.RoundNumber),
                        Share = (double)group.Count() / totalRolls
                    })
                    .OrderByDescending(group => group.RollCount)
                    .ThenBy(group => group.WeaponText, StringComparer.CurrentCultureIgnoreCase)
                    .Select(group => new GameHistoryBoxWeaponAverageViewModel(
                        group.WeaponText,
                        FormatCount(group.RollCount),
                        FormatAverageRound(group.AverageRound),
                        FormatPercent(group.Share)))
            ];
        }

        private static bool IsCompletedBoxRoll(GameHistoryBoxEvent boxEvent)
        {
            return string.Equals(boxEvent.EventName, CompletedBoxRollEventName, StringComparison.Ordinal);
        }

        private static string FormatBoxWeapon(GameHistoryBoxEvent boxEvent)
        {
            return string.IsNullOrWhiteSpace(boxEvent.WeaponDisplayName)
                ? AppStrings.Get("GameHistoryBoxEventUnknownWeapon")
                : boxEvent.WeaponDisplayName!;
        }

        private GameHistoryStatsDisplayViewModel CreateStatsDisplay(GameHistoryStats stats, bool isDelta)
        {
            return new GameHistoryStatsDisplayViewModel(
                FormatStat(stats.Points, isDelta),
                FormatStat(stats.Kills, isDelta),
                FormatStat(stats.Downs, isDelta),
                FormatStat(stats.Revives, isDelta),
                FormatStat(stats.Headshots, isDelta));
        }

        private string FormatStat(int value, bool isDelta)
        {
            return isDelta
                ? value.ToString("+#,0;-#,0;0", CultureInfo.CurrentCulture)
                : _statFormatter.FormatStat(value);
        }

        private static string FormatCount(int value)
        {
            return value.ToString("N0", CultureInfo.CurrentCulture);
        }

        private static string FormatAverageRound(double value)
        {
            return value.ToString("0.#", CultureInfo.CurrentCulture);
        }

        private static string FormatAverageRollsPerRound(int rollCount, int finalRound)
        {
            if (rollCount == 0 || finalRound <= 0)
            {
                return MissingValueText;
            }

            return ((double)rollCount / finalRound).ToString("0.#", CultureInfo.CurrentCulture);
        }

        private static string FormatPercent(double value)
        {
            return value.ToString("P0", CultureInfo.CurrentCulture);
        }

        private static string FormatDate(DateTimeOffset date)
        {
            return date.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        }

        private static string FormatDuration(TimeSpan? duration)
        {
            if (duration is not TimeSpan value || value < TimeSpan.Zero)
            {
                return MissingValueText;
            }

            return GameTimerDurationFormatter.Format(value);
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

        internal GameHistorySummaryViewModel(
            GameHistoryEntry source,
            string dateText,
            string mapNameText,
            string finalRoundText,
            string gameDurationText,
            string pointsText,
            string killsText,
            string downsText,
            string revivesText,
            string headshotsText)
        {
            Source = source;
            DateText = dateText;
            MapNameText = mapNameText;
            FinalRoundText = finalRoundText;
            GameDurationText = gameDurationText;
            PointsText = pointsText;
            KillsText = killsText;
            DownsText = downsText;
            RevivesText = revivesText;
            HeadshotsText = headshotsText;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id => Source.Id;

        public string DateText { get; }

        public string MapNameText { get; }

        public string FinalRoundText { get; }

        public string GameDurationText { get; }

        public string PointsText { get; }

        public string KillsText { get; }

        public string DownsText { get; }

        public string RevivesText { get; }

        public string HeadshotsText { get; }

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

        internal GameHistoryEntry Source { get; }
    }

    public sealed class GameHistoryDetailRequestedEventArgs(string id) : EventArgs
    {
        public string Id { get; } = id;
    }

    public sealed class GameHistoryDetailViewModel
    {
        internal GameHistoryDetailViewModel(
            string id,
            string dateText,
            string mapNameText,
            string finalRoundText,
            string gameDurationText,
            GameHistoryStatsDisplayViewModel finalStats,
            IReadOnlyList<GameHistoryRoundViewModel> rounds,
            IReadOnlyList<GameHistoryBoxEventViewModel> boxEvents,
            IReadOnlyList<GameHistoryBoxWeaponAverageViewModel> boxWeaponAverages,
            string boxRollCountText,
            string boxUniqueWeaponCountText,
            string boxAverageRollsPerRoundText,
            string boxMostSeenWeaponText)
        {
            Id = id;
            DateText = dateText;
            MapNameText = mapNameText;
            FinalRoundText = finalRoundText;
            GameDurationText = gameDurationText;
            FinalStats = finalStats;
            Rounds = new ReadOnlyObservableCollection<GameHistoryRoundViewModel>(
                new ObservableCollection<GameHistoryRoundViewModel>(rounds));
            BoxEvents = new ReadOnlyObservableCollection<GameHistoryBoxEventViewModel>(
                new ObservableCollection<GameHistoryBoxEventViewModel>(boxEvents));
            BoxWeaponAverages = new ReadOnlyObservableCollection<GameHistoryBoxWeaponAverageViewModel>(
                new ObservableCollection<GameHistoryBoxWeaponAverageViewModel>(boxWeaponAverages));
            BoxRollCountText = boxRollCountText;
            BoxUniqueWeaponCountText = boxUniqueWeaponCountText;
            BoxAverageRollsPerRoundText = boxAverageRollsPerRoundText;
            BoxMostSeenWeaponText = boxMostSeenWeaponText;
        }

        public string Id { get; }

        public string DateText { get; }

        public string MapNameText { get; }

        public string FinalRoundText { get; }

        public string GameDurationText { get; }

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
    }

    public sealed class GameHistoryRoundViewModel
    {
        internal GameHistoryRoundViewModel(
            int roundNumber,
            string roundTitleText,
            string durationText,
            GameHistoryStatsDisplayViewModel cumulativeStats,
            GameHistoryStatsDisplayViewModel deltaStats)
        {
            RoundNumber = roundNumber;
            RoundTitleText = roundTitleText;
            DurationText = durationText;
            CumulativeStats = cumulativeStats;
            DeltaStats = deltaStats;
        }

        public int RoundNumber { get; }

        public string RoundTitleText { get; }

        public string DurationText { get; }

        public GameHistoryStatsDisplayViewModel CumulativeStats { get; }

        public GameHistoryStatsDisplayViewModel DeltaStats { get; }
    }

    public sealed class GameHistoryStatsDisplayViewModel(
        string pointsText,
        string killsText,
        string downsText,
        string revivesText,
        string headshotsText)
    {
        public string PointsText { get; } = pointsText;

        public string KillsText { get; } = killsText;

        public string DownsText { get; } = downsText;

        public string RevivesText { get; } = revivesText;

        public string HeadshotsText { get; } = headshotsText;
    }

    public sealed class GameHistoryBoxEventViewModel(
        string receivedAtText,
        string roundText,
        string weaponText,
        string primaryText)
    {
        public string ReceivedAtText { get; } = receivedAtText;

        public string RoundText { get; } = roundText;

        public string WeaponText { get; } = weaponText;

        public string PrimaryText { get; } = primaryText;
    }

    public sealed class GameHistoryBoxWeaponAverageViewModel(
        string weaponText,
        string rollCountText,
        string averageRoundText,
        string shareText)
    {
        public string WeaponText { get; } = weaponText;

        public string RollCountText { get; } = rollCountText;

        public string AverageRoundText { get; } = averageRoundText;

        public string ShareText { get; } = shareText;
    }
}
