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
        public const string MissingValueText = "--";

        private readonly StatFormatter _statFormatter = new(MissingValueText);
        private GameHistoryDetailViewModel? _selectedGame;
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

        public string EmptyStateTitle => AppStrings.Get("GameHistoryEmptyTitle");

        public string EmptyStateText => AppStrings.Get("GameHistoryEmptyText");

        internal void ReplaceSavedGames(IEnumerable<GameHistoryEntry> savedGames)
        {
            ArgumentNullException.ThrowIfNull(savedGames);

            string? selectedId = SelectedGame?.Id;

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

            if (selectedId is not null)
            {
                SelectGameById(selectedId);
            }
        }

        public void SelectGame(GameHistorySummaryViewModel summary)
        {
            ArgumentNullException.ThrowIfNull(summary);

            SelectedGame = summary.CreateDetail();
        }

        public void SelectGameById(string id)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);

            GameHistorySummaryViewModel? summary = SavedGames.FirstOrDefault(
                game => string.Equals(game.Id, id, StringComparison.Ordinal));
            SelectedGame = summary?.CreateDetail();
        }

        public void ShowList()
        {
            SelectedGame = null;
        }

        internal void ApplyRecordingStatus(GameHistoryRecordingStatus status)
        {
            ArgumentNullException.ThrowIfNull(status);

            (RecordingStatusTitle, RecordingStatusText) = status.State switch
            {
                GameHistoryRecordingState.Recording => (
                    AppStrings.Get("GameHistoryRecordingStatusActiveTitle"),
                    status.ActiveRoundNumber is int roundNumber
                        ? AppStrings.Format("GameHistoryRecordingStatusActiveRoundFormat", roundNumber)
                        : AppStrings.Get("GameHistoryRecordingStatusActiveText")),
                GameHistoryRecordingState.WaitingForRoundOne => (
                    AppStrings.Get("GameHistoryRecordingStatusActiveTitle"),
                    AppStrings.Get("GameHistoryRecordingStatusWaitingForRoundOneText")),
                GameHistoryRecordingState.Saved => (
                    AppStrings.Get("GameHistoryRecordingStatusSavedTitle"),
                    AppStrings.Get("GameHistoryRecordingStatusSavedText")),
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

            if (snapshot.MapIdentityResult?.IsConfirmedTown == true)
            {
                ApplyRecordingStatus(GameHistoryRecordingStatus.WaitingForRoundOne);
                return;
            }

            ApplyRecordingStatus(GameHistoryRecordingStatus.Unavailable(
                GameHistoryRecordingUnavailableReason.RequiresTown));
        }

        private static (string Title, string Text) ProjectUnavailableStatus(
            GameHistoryRecordingUnavailableReason unavailableReason)
        {
            return unavailableReason switch
            {
                GameHistoryRecordingUnavailableReason.RequiresHookBackedEventMonitor => (
                    AppStrings.Get("GameHistoryRecordingStatusRequiresHookTitle"),
                    AppStrings.Get("GameHistoryRecordingStatusRequiresHookText")),
                GameHistoryRecordingUnavailableReason.RequiresTown
                    or GameHistoryRecordingUnavailableReason.MissingMapIdentity
                    or GameHistoryRecordingUnavailableReason.MissingFriendlyMapName => (
                        AppStrings.Get("GameHistoryRecordingStatusRequiresTownTitle"),
                        AppStrings.Get("GameHistoryRecordingStatusRequiresTownText")),
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
                        AppStrings.Get("GameHistoryRecordingStatusRequiresTownTitle"),
                        AppStrings.Get("GameHistoryRecordingStatusRequiresTownText")),
                _ => (
                    AppStrings.Get("GameHistoryRecordingStatusDiscardedTitle"),
                    AppStrings.Get("GameHistoryRecordingStatusDiscardedSequenceText"))
            };
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
                [.. game.BoxEvents
                    .OrderBy(static boxEvent => boxEvent.ReceivedAt)
                    .Select(CreateBoxEvent)]);
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

        private GameHistoryBoxEventViewModel CreateBoxEvent(GameHistoryBoxEvent boxEvent)
        {
            string receivedAtText = FormatDate(boxEvent.ReceivedAt);
            string roundText = AppStrings.Format("GameHistoryRoundTitleFormat", boxEvent.RoundNumber);
            string weaponText = string.IsNullOrWhiteSpace(boxEvent.WeaponDisplayName)
                ? AppStrings.Get("GameHistoryBoxEventUnknownWeapon")
                : boxEvent.WeaponDisplayName!;
            string rawWeaponTokenText = string.IsNullOrWhiteSpace(boxEvent.RawWeaponToken)
                ? MissingValueText
                : boxEvent.RawWeaponToken!;
            string stringValueText = boxEvent.StringValue.ToString(CultureInfo.CurrentCulture);
            string primaryText = AppStrings.Format(
                "GameHistoryBoxEventPrimaryFormat",
                roundText,
                boxEvent.EventName,
                weaponText);

            return new GameHistoryBoxEventViewModel(
                receivedAtText,
                roundText,
                boxEvent.EventName,
                weaponText,
                rawWeaponTokenText,
                stringValueText,
                boxEvent.OwnerId,
                primaryText);
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

        private static string FormatDate(DateTimeOffset date)
        {
            return date.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
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

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class GameHistorySummaryViewModel
    {
        private readonly GameHistoryDetailViewModel _detail;

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
            IReadOnlyList<GameHistoryBoxEventViewModel> boxEvents)
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
        }

        public string Id { get; }

        public string DateText { get; }

        public string MapNameText { get; }

        public string FinalRoundText { get; }

        public string GameDurationText { get; }

        public GameHistoryStatsDisplayViewModel FinalStats { get; }

        public ReadOnlyObservableCollection<GameHistoryRoundViewModel> Rounds { get; }

        public ReadOnlyObservableCollection<GameHistoryBoxEventViewModel> BoxEvents { get; }

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
        string eventNameText,
        string weaponText,
        string rawWeaponTokenText,
        string stringValueText,
        uint ownerId,
        string primaryText)
    {
        public string ReceivedAtText { get; } = receivedAtText;

        public string RoundText { get; } = roundText;

        public string EventNameText { get; } = eventNameText;

        public string WeaponText { get; } = weaponText;

        public string RawWeaponTokenText { get; } = rawWeaponTokenText;

        public string StringValueText { get; } = stringValueText;

        public uint OwnerId { get; } = ownerId;

        public string PrimaryText { get; } = primaryText;
    }
}
