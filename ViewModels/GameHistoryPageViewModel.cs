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
        private bool _isHistoryRailOpen = true;
        private GameHistoryDetailViewModel? _selectedGame;
        private GameHistorySummaryViewModel? _selectedGameSummary;
        private string _recordingStatusTitle = AppStrings.Get("GameHistoryRecordingStatusWaitingTitle");
        private string _recordingStatusText = AppStrings.Get("GameHistoryRecordingStatusWaitingText");

        public event PropertyChangedEventHandler? PropertyChanged;

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
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsListVisible));
                OnPropertyChanged(nameof(IsDetailVisible));
                OnPropertyChanged(nameof(IsEmptyVisible));
                OnPropertyChanged(nameof(IsHistoryRailVisible));
                OnPropertyChanged(nameof(IsHistoryRailReopenButtonVisible));
            }
        }

        public GameHistorySummaryViewModel? SelectedGameSummary
        {
            get => _selectedGameSummary;
            set
            {
                if (ReferenceEquals(_selectedGameSummary, value))
                {
                    return;
                }

                if (_selectedGameSummary is not null)
                {
                    _selectedGameSummary.IsSelected = false;
                }

                _selectedGameSummary = value;
                if (_selectedGameSummary is not null)
                {
                    _selectedGameSummary.IsSelected = true;
                }

                OnPropertyChanged();
                SelectedGame = _selectedGameSummary?.CreateDetail();
            }
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

        public bool IsEmptyVisible => IsListVisible && !HasSavedGames;

        public bool IsListVisible => SelectedGame is null;

        public bool IsDetailVisible => SelectedGame is not null;

        public bool IsHistoryRailVisible => IsDetailVisible && _isHistoryRailOpen;

        public bool IsHistoryRailReopenButtonVisible => IsDetailVisible && !_isHistoryRailOpen;

        public string TrackedGameCountText => AppStrings.Format("GameHistoryTrackedGameCountFormat", SavedGames.Count);

        public string EmptyStateTitle => AppStrings.Get("GameHistoryEmptyTitle");

        public string EmptyStateText => AppStrings.Get("GameHistoryEmptyText");

        internal void ReplaceSavedGames(IEnumerable<GameHistoryEntry> savedGames)
        {
            ArgumentNullException.ThrowIfNull(savedGames);

            string? selectedId = SelectedGameSummary?.Id ?? SelectedGame?.Id;
            bool hadSavedGames = HasSavedGames;

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
            OnPropertyChanged(nameof(IsEmptyVisible));
            OnPropertyChanged(nameof(TrackedGameCountText));

            GameHistorySummaryViewModel? summaryToSelect = null;
            if (selectedId is not null)
            {
                summaryToSelect = SavedGames.FirstOrDefault(
                    game => string.Equals(game.Id, selectedId, StringComparison.Ordinal));
            }
            else if (!hadSavedGames && summaries.Length > 0)
            {
                summaryToSelect = summaries[0];
            }

            SelectedGameSummary = summaryToSelect;
        }

        public void SelectGame(GameHistorySummaryViewModel summary)
        {
            ArgumentNullException.ThrowIfNull(summary);

            SetHistoryRailOpen(true);
            SelectedGameSummary = summary;
        }

        public void SelectGameById(string id)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);

            GameHistorySummaryViewModel? summary = SavedGames.FirstOrDefault(
                game => string.Equals(game.Id, id, StringComparison.Ordinal));
            SetHistoryRailOpen(true);
            SelectedGameSummary = summary;
        }

        public void ShowList()
        {
            SelectedGameSummary = null;
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

            (RecordingStatusTitle, RecordingStatusText) = status.State switch
            {
                GameHistoryRecordingState.Recording => (
                    AppStrings.Get("GameHistoryRecordingStatusActiveTitle"),
                    ProjectActiveRecordingText(status)),
                GameHistoryRecordingState.WaitingForRoundOne => (
                    AppStrings.Get("GameHistoryRecordingStatusActiveTitle"),
                    ProjectWaitingForRoundOneText(status.MapName)),
                GameHistoryRecordingState.Saved => (
                    AppStrings.Get("GameHistoryRecordingStatusSavedTitle"),
                    ProjectSavedText(status.MapName)),
                GameHistoryRecordingState.Discarded => ProjectDiscardedStatus(status.DiscardReason),
                GameHistoryRecordingState.Unavailable => ProjectUnavailableStatus(status.UnavailableReason),
                _ => (
                    AppStrings.Get("GameHistoryRecordingStatusWaitingTitle"),
                    AppStrings.Get("GameHistoryRecordingStatusWaitingText"))
            };
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

        private static string ProjectActiveRecordingText(GameHistoryRecordingStatus status)
        {
            string? mapName = NormalizeMapName(status.MapName);
            if (status.ActiveRoundNumber is int roundNumber)
            {
                return mapName is null
                    ? AppStrings.Format("GameHistoryRecordingStatusActiveRoundFormat", roundNumber)
                    : AppStrings.Format("GameHistoryRecordingStatusActiveMapRoundFormat", mapName, roundNumber);
            }

            return mapName is null
                ? AppStrings.Get("GameHistoryRecordingStatusActiveText")
                : AppStrings.Format("GameHistoryRecordingStatusActiveMapFormat", mapName);
        }

        private static string ProjectWaitingForRoundOneText(string? mapName)
        {
            string? normalizedMapName = NormalizeMapName(mapName);
            return normalizedMapName is null
                ? AppStrings.Get("GameHistoryRecordingStatusWaitingForRoundOneText")
                : AppStrings.Format("GameHistoryRecordingStatusWaitingForRoundOneMapFormat", normalizedMapName);
        }

        private static string ProjectSavedText(string? mapName)
        {
            string? normalizedMapName = NormalizeMapName(mapName);
            return normalizedMapName is null
                ? AppStrings.Get("GameHistoryRecordingStatusSavedText")
                : AppStrings.Format("GameHistoryRecordingStatusSavedMapFormat", normalizedMapName);
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

        private static (string Title, string Text) ProjectUnavailableStatus(
            GameHistoryRecordingUnavailableReason unavailableReason)
        {
            return unavailableReason switch
            {
                GameHistoryRecordingUnavailableReason.RequiresHookBackedEventMonitor => (
                    AppStrings.Get("GameHistoryRecordingStatusRequiresHookTitle"),
                    AppStrings.Get("GameHistoryRecordingStatusRequiresHookText")),
                GameHistoryRecordingUnavailableReason.RequiresSupportedMap
                    or GameHistoryRecordingUnavailableReason.MissingMapIdentity
                    or GameHistoryRecordingUnavailableReason.MissingFriendlyMapName => (
                        AppStrings.Get("GameHistoryRecordingStatusRequiresSupportedMapTitle"),
                        AppStrings.Get("GameHistoryRecordingStatusRequiresSupportedMapText")),
                _ => (
                    AppStrings.Get("GameHistoryRecordingStatusWaitingTitle"),
                    AppStrings.Get("GameHistoryRecordingStatusWaitingText"))
            };
        }

        private static (string Title, string Text) ProjectDiscardedStatus(
            GameHistoryRecordingDiscardReason discardReason)
        {
            return discardReason switch
            {
                GameHistoryRecordingDiscardReason.SequenceGap
                    or GameHistoryRecordingDiscardReason.DroppedLifecycleData
                    or GameHistoryRecordingDiscardReason.PollingFallback => (
                        AppStrings.Get("GameHistoryRecordingStatusDiscardedTitle"),
                        AppStrings.Get("GameHistoryRecordingStatusDiscardedSequenceText")),
                GameHistoryRecordingDiscardReason.MissingRequiredStats => (
                    AppStrings.Get("GameHistoryRecordingStatusDiscardedTitle"),
                    AppStrings.Get("GameHistoryRecordingStatusDiscardedMissingStatsText")),
                GameHistoryRecordingDiscardReason.DetectedGameChanged
                    or GameHistoryRecordingDiscardReason.Disconnected
                    or GameHistoryRecordingDiscardReason.AppClosed => (
                        AppStrings.Get("GameHistoryRecordingStatusDiscardedTitle"),
                        AppStrings.Get("GameHistoryRecordingStatusDiscardedConnectionEndedText")),
                GameHistoryRecordingDiscardReason.MissingMapIdentity
                    or GameHistoryRecordingDiscardReason.UnsupportedMapIdentity
                    or GameHistoryRecordingDiscardReason.MissingFriendlyMapName => (
                        AppStrings.Get("GameHistoryRecordingStatusRequiresSupportedMapTitle"),
                        AppStrings.Get("GameHistoryRecordingStatusRequiresSupportedMapText")),
                _ => (
                    AppStrings.Get("GameHistoryRecordingStatusDiscardedTitle"),
                    AppStrings.Get("GameHistoryRecordingStatusDiscardedSequenceText"))
            };
        }

        private static string? NormalizeMapName(string? mapName)
        {
            return string.IsNullOrWhiteSpace(mapName)
                ? null
                : mapName.Trim();
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
                _statFormatter.FormatStat(game.FinalStats.Headshots),
                CreateDetail(game));
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

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class GameHistorySummaryViewModel : INotifyPropertyChanged
    {
        private readonly GameHistoryDetailViewModel _detail;
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
            string headshotsText,
            GameHistoryDetailViewModel detail)
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
            _detail = detail;
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

        internal GameHistoryDetailViewModel CreateDetail() => _detail;
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
