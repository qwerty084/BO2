using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BO2.Services
{
    internal sealed class GameHistoryDisplayProjector
    {
        private const string CompletedBoxRollEventName = "randomization_done";

        private readonly DisplayTextRenderer _renderer;

        public GameHistoryDisplayProjector()
            : this(new DisplayTextRenderer())
        {
        }

        private GameHistoryDisplayProjector(DisplayTextRenderer renderer)
        {
            ArgumentNullException.ThrowIfNull(renderer);

            _renderer = renderer;
        }

        public IReadOnlyList<GameHistorySummaryDisplayState> ProjectSavedSummaries(
            IEnumerable<GameHistorySummary> summaries)
        {
            ArgumentNullException.ThrowIfNull(summaries);

            return
            [
                .. summaries
                    .OrderByDescending(static summary => summary.EndedAt)
                    .ThenByDescending(static summary => summary.StartedAt)
                    .ThenBy(static summary => summary.Id, StringComparer.Ordinal)
                    .Select(ProjectSavedSummary)
            ];
        }

        public IReadOnlyList<GameHistorySummaryDisplayState> ProjectSavedGameSummaries(
            IEnumerable<GameHistoryEntry> savedGames)
        {
            ArgumentNullException.ThrowIfNull(savedGames);

            return ProjectSavedSummaries(savedGames.Select(CreateSummary));
        }

        public GameHistoryRecordingStatusDisplayState ProjectRecordingStatus(GameHistoryRecordingStatus status)
        {
            ArgumentNullException.ThrowIfNull(status);

            GameHistoryRecordingStatusDisplayProjection projection = status.State switch
            {
                GameHistoryRecordingState.Recording => new(
                    DisplayText.Resource("GameHistoryRecordingStatusActiveTitle"),
                    ProjectActiveRecordingText(status)),
                GameHistoryRecordingState.WaitingForRoundOne => new(
                    DisplayText.Resource("GameHistoryRecordingStatusActiveTitle"),
                    ProjectWaitingForRoundOneText(status.MapName)),
                GameHistoryRecordingState.SavePending => new(
                    DisplayText.Resource("GameHistoryRecordingStatusSavePendingTitle"),
                    ProjectSavePendingText(status.MapName)),
                GameHistoryRecordingState.FailedSave => new(
                    DisplayText.Resource("GameHistoryRecordingStatusFailedSaveTitle"),
                    ProjectFailedSaveText(status.MapName)),
                GameHistoryRecordingState.Saved => new(
                    DisplayText.Resource("GameHistoryRecordingStatusSavedTitle"),
                    ProjectSavedText(status.MapName)),
                GameHistoryRecordingState.Discarded => ProjectDiscardedStatus(status.DiscardReason),
                GameHistoryRecordingState.Unavailable => ProjectUnavailableStatus(status.UnavailableReason),
                _ => GameHistoryRecordingStatusDisplayProjection.Waiting
            };

            return Render(projection);
        }

        public GameHistoryDetailDisplayState ProjectSelectedDetail(GameHistoryEntry game)
        {
            ArgumentNullException.ThrowIfNull(game);

            GameHistoryRoundDisplayState[] rounds =
            [
                .. game.Rounds
                    .OrderBy(static round => round.RoundNumber)
                    .Select(ProjectRound)
            ];
            GameHistoryBoxEvent[] boxRolls =
            [
                .. game.BoxEvents
                    .Where(IsCompletedBoxRoll)
                    .OrderBy(static boxEvent => boxEvent.ReceivedAt)
            ];
            GameHistoryBoxEventDisplayState[] boxEvents =
            [
                .. boxRolls.Select(ProjectBoxRoll)
            ];
            GameHistoryBoxWeaponAverageDisplayState[] boxWeaponAverages = ProjectBoxWeaponAverages(boxRolls);

            return new GameHistoryDetailDisplayState(
                game.Id,
                FormatDate(game.EndedAt),
                game.MapIdentity.FriendlyName,
                _renderer.Render(DisplayText.Format("GameHistoryFinalRoundFormat", game.FinalRound)),
                FormatDuration(game.GameDuration),
                ProjectStats(game.FinalStats, isDelta: false),
                Array.AsReadOnly(rounds),
                Array.AsReadOnly(boxEvents),
                Array.AsReadOnly(boxWeaponAverages),
                FormatCount(boxRolls.Length),
                FormatCount(boxWeaponAverages.Length),
                FormatAverageRollsPerRound(boxRolls.Length, game.FinalRound),
                boxWeaponAverages.Length == 0 ? FormatMissingValue() : boxWeaponAverages[0].WeaponText);
        }

        private static GameHistorySummary CreateSummary(GameHistoryEntry game)
        {
            ArgumentNullException.ThrowIfNull(game);

            return new GameHistorySummary(
                game.Id,
                game.StartedAt,
                game.EndedAt,
                game.MapIdentity,
                game.FinalRound,
                game.FinalStats,
                game.GameDuration);
        }

        private GameHistorySummaryDisplayState ProjectSavedSummary(GameHistorySummary summary)
        {
            ArgumentNullException.ThrowIfNull(summary);

            return new GameHistorySummaryDisplayState(
                summary.Id,
                FormatDate(summary.EndedAt),
                summary.MapIdentity.FriendlyName,
                _renderer.Render(DisplayText.Format("GameHistoryFinalRoundFormat", summary.FinalRound)),
                FormatDuration(summary.GameDuration),
                _renderer.Render(DisplayText.Integer(summary.FinalStats.Points)),
                _renderer.Render(DisplayText.Integer(summary.FinalStats.Kills)),
                _renderer.Render(DisplayText.Integer(summary.FinalStats.Downs)),
                _renderer.Render(DisplayText.Integer(summary.FinalStats.Revives)),
                _renderer.Render(DisplayText.Integer(summary.FinalStats.Headshots)));
        }

        private GameHistoryRoundDisplayState ProjectRound(GameHistoryRound round)
        {
            return new GameHistoryRoundDisplayState(
                round.RoundNumber,
                _renderer.Render(DisplayText.Format("GameHistoryRoundTitleFormat", round.RoundNumber)),
                FormatDuration(round.RoundDuration),
                ProjectStats(round.CumulativeStats, isDelta: false),
                ProjectStats(round.DeltaStats, isDelta: true));
        }

        private GameHistoryBoxEventDisplayState ProjectBoxRoll(GameHistoryBoxEvent boxEvent)
        {
            string roundText = _renderer.Render(DisplayText.Format(
                "GameHistoryRoundTitleFormat",
                boxEvent.RoundNumber));
            string weaponText = FormatBoxWeapon(boxEvent);

            return new GameHistoryBoxEventDisplayState(
                FormatDate(boxEvent.ReceivedAt),
                roundText,
                weaponText,
                _renderer.Render(DisplayText.Format(
                    "GameHistoryBoxRollPrimaryFormat",
                    roundText,
                    weaponText)));
        }

        private GameHistoryBoxWeaponAverageDisplayState[] ProjectBoxWeaponAverages(
            IReadOnlyCollection<GameHistoryBoxEvent> boxRolls)
        {
            int totalRolls = boxRolls.Count;
            if (totalRolls == 0)
            {
                return [];
            }

            return
            [
                .. boxRolls
                    .GroupBy(FormatBoxWeapon, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new
                    {
                        WeaponText = group.Key,
                        RollCount = group.Count(),
                        AverageRound = group.Average(static boxEvent => boxEvent.RoundNumber),
                        Share = (double)group.Count() / totalRolls
                    })
                    .OrderByDescending(static group => group.RollCount)
                    .ThenBy(static group => group.WeaponText, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new GameHistoryBoxWeaponAverageDisplayState(
                        group.WeaponText,
                        FormatCount(group.RollCount),
                        FormatAverageRound(group.AverageRound),
                        FormatPercent(group.Share)))
            ];
        }

        private GameHistoryStatsDisplayState ProjectStats(GameHistoryStats stats, bool isDelta)
        {
            return new GameHistoryStatsDisplayState(
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
                : _renderer.Render(DisplayText.Integer(value));
        }

        private static bool IsCompletedBoxRoll(GameHistoryBoxEvent boxEvent)
        {
            return string.Equals(boxEvent.EventName, CompletedBoxRollEventName, StringComparison.Ordinal);
        }

        private string FormatBoxWeapon(GameHistoryBoxEvent boxEvent)
        {
            return string.IsNullOrWhiteSpace(boxEvent.WeaponDisplayName)
                ? _renderer.Render(DisplayText.Resource("GameHistoryBoxEventUnknownWeapon"))
                : boxEvent.WeaponDisplayName!;
        }

        private static string FormatCount(int value)
        {
            return value.ToString("N0", CultureInfo.CurrentCulture);
        }

        private static string FormatAverageRound(double value)
        {
            return value.ToString("0.#", CultureInfo.CurrentCulture);
        }

        private string FormatAverageRollsPerRound(int rollCount, int finalRound)
        {
            if (rollCount == 0 || finalRound <= 0)
            {
                return FormatMissingValue();
            }

            return ((double)rollCount / finalRound).ToString("0.#", CultureInfo.CurrentCulture);
        }

        private static string FormatPercent(double value)
        {
            return value.ToString("P0", CultureInfo.CurrentCulture);
        }

        private GameHistoryRecordingStatusDisplayState Render(
            GameHistoryRecordingStatusDisplayProjection projection)
        {
            return new GameHistoryRecordingStatusDisplayState(
                _renderer.Render(projection.Title),
                _renderer.Render(projection.BodyText));
        }

        private static DisplayText ProjectActiveRecordingText(GameHistoryRecordingStatus status)
        {
            string? mapName = NormalizeMapName(status.MapName);
            if (status.ActiveRoundNumber is int roundNumber)
            {
                return mapName is null
                    ? DisplayText.Format("GameHistoryRecordingStatusActiveRoundFormat", roundNumber)
                    : DisplayText.Format("GameHistoryRecordingStatusActiveMapRoundFormat", mapName, roundNumber);
            }

            return mapName is null
                ? DisplayText.Resource("GameHistoryRecordingStatusActiveText")
                : DisplayText.Format("GameHistoryRecordingStatusActiveMapFormat", mapName);
        }

        private static DisplayText ProjectWaitingForRoundOneText(string? mapName)
        {
            string? normalizedMapName = NormalizeMapName(mapName);
            return normalizedMapName is null
                ? DisplayText.Resource("GameHistoryRecordingStatusWaitingForRoundOneText")
                : DisplayText.Format("GameHistoryRecordingStatusWaitingForRoundOneMapFormat", normalizedMapName);
        }

        private static DisplayText ProjectSavedText(string? mapName)
        {
            string? normalizedMapName = NormalizeMapName(mapName);
            return normalizedMapName is null
                ? DisplayText.Resource("GameHistoryRecordingStatusSavedText")
                : DisplayText.Format("GameHistoryRecordingStatusSavedMapFormat", normalizedMapName);
        }

        private static DisplayText ProjectSavePendingText(string? mapName)
        {
            string? normalizedMapName = NormalizeMapName(mapName);
            return normalizedMapName is null
                ? DisplayText.Resource("GameHistoryRecordingStatusSavePendingText")
                : DisplayText.Format("GameHistoryRecordingStatusSavePendingMapFormat", normalizedMapName);
        }

        private static DisplayText ProjectFailedSaveText(string? mapName)
        {
            string? normalizedMapName = NormalizeMapName(mapName);
            return normalizedMapName is null
                ? DisplayText.Resource("GameHistoryRecordingStatusFailedSaveText")
                : DisplayText.Format("GameHistoryRecordingStatusFailedSaveMapFormat", normalizedMapName);
        }

        private static GameHistoryRecordingStatusDisplayProjection ProjectUnavailableStatus(
            GameHistoryRecordingUnavailableReason unavailableReason)
        {
            return unavailableReason switch
            {
                GameHistoryRecordingUnavailableReason.RequiresHookBackedEventMonitor => new(
                    DisplayText.Resource("GameHistoryRecordingStatusRequiresHookTitle"),
                    DisplayText.Resource("GameHistoryRecordingStatusRequiresHookText")),
                GameHistoryRecordingUnavailableReason.RequiresSupportedMap
                    or GameHistoryRecordingUnavailableReason.MissingMapIdentity
                    or GameHistoryRecordingUnavailableReason.MissingFriendlyMapName => new(
                        DisplayText.Resource("GameHistoryRecordingStatusRequiresSupportedMapTitle"),
                        DisplayText.Resource("GameHistoryRecordingStatusRequiresSupportedMapText")),
                _ => GameHistoryRecordingStatusDisplayProjection.Waiting
            };
        }

        private static GameHistoryRecordingStatusDisplayProjection ProjectDiscardedStatus(
            GameHistoryRecordingDiscardReason discardReason)
        {
            return discardReason switch
            {
                GameHistoryRecordingDiscardReason.SequenceGap
                    or GameHistoryRecordingDiscardReason.DroppedLifecycleData
                    or GameHistoryRecordingDiscardReason.PollingFallback => new(
                        DisplayText.Resource("GameHistoryRecordingStatusDiscardedTitle"),
                        DisplayText.Resource("GameHistoryRecordingStatusDiscardedSequenceText")),
                GameHistoryRecordingDiscardReason.MissingRequiredStats => new(
                    DisplayText.Resource("GameHistoryRecordingStatusDiscardedTitle"),
                    DisplayText.Resource("GameHistoryRecordingStatusDiscardedMissingStatsText")),
                GameHistoryRecordingDiscardReason.DetectedGameChanged
                    or GameHistoryRecordingDiscardReason.Disconnected
                    or GameHistoryRecordingDiscardReason.AppClosed => new(
                        DisplayText.Resource("GameHistoryRecordingStatusDiscardedTitle"),
                        DisplayText.Resource("GameHistoryRecordingStatusDiscardedConnectionEndedText")),
                GameHistoryRecordingDiscardReason.MissingMapIdentity
                    or GameHistoryRecordingDiscardReason.UnsupportedMapIdentity
                    or GameHistoryRecordingDiscardReason.MissingFriendlyMapName => new(
                        DisplayText.Resource("GameHistoryRecordingStatusRequiresSupportedMapTitle"),
                        DisplayText.Resource("GameHistoryRecordingStatusRequiresSupportedMapText")),
                _ => new GameHistoryRecordingStatusDisplayProjection(
                    DisplayText.Resource("GameHistoryRecordingStatusDiscardedTitle"),
                    DisplayText.Resource("GameHistoryRecordingStatusDiscardedSequenceText"))
            };
        }

        private static string? NormalizeMapName(string? mapName)
        {
            return string.IsNullOrWhiteSpace(mapName)
                ? null
                : mapName.Trim();
        }

        private static string FormatDate(DateTimeOffset date)
        {
            return date.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        }

        private string FormatDuration(TimeSpan? duration)
        {
            if (duration is not TimeSpan value || value < TimeSpan.Zero)
            {
                return FormatMissingValue();
            }

            return GameTimerDurationFormatter.Format(value);
        }

        private string FormatMissingValue()
        {
            return _renderer.Render(GameConnectionDisplayTextProjector.EmptyValueText);
        }
    }

    internal sealed record GameHistorySummaryDisplayState(
        string Id,
        string DateText,
        string MapNameText,
        string FinalRoundText,
        string GameDurationText,
        string PointsText,
        string KillsText,
        string DownsText,
        string RevivesText,
        string HeadshotsText);

    internal sealed record GameHistoryDetailDisplayState(
        string Id,
        string DateText,
        string MapNameText,
        string FinalRoundText,
        string GameDurationText,
        GameHistoryStatsDisplayState FinalStats,
        IReadOnlyList<GameHistoryRoundDisplayState> Rounds,
        IReadOnlyList<GameHistoryBoxEventDisplayState> BoxEvents,
        IReadOnlyList<GameHistoryBoxWeaponAverageDisplayState> BoxWeaponAverages,
        string BoxRollCountText,
        string BoxUniqueWeaponCountText,
        string BoxAverageRollsPerRoundText,
        string BoxMostSeenWeaponText);

    internal sealed record GameHistoryRoundDisplayState(
        int RoundNumber,
        string RoundTitleText,
        string DurationText,
        GameHistoryStatsDisplayState CumulativeStats,
        GameHistoryStatsDisplayState DeltaStats);

    internal sealed record GameHistoryStatsDisplayState(
        string PointsText,
        string KillsText,
        string DownsText,
        string RevivesText,
        string HeadshotsText);

    internal sealed record GameHistoryBoxEventDisplayState(
        string ReceivedAtText,
        string RoundText,
        string WeaponText,
        string PrimaryText);

    internal sealed record GameHistoryBoxWeaponAverageDisplayState(
        string WeaponText,
        string RollCountText,
        string AverageRoundText,
        string ShareText);

    internal sealed record GameHistoryRecordingStatusDisplayState(string Title, string BodyText);

    internal sealed record GameHistoryRecordingStatusDisplayProjection(DisplayText Title, DisplayText BodyText)
    {
        public static GameHistoryRecordingStatusDisplayProjection Waiting { get; } = new(
            DisplayText.Resource("GameHistoryRecordingStatusWaitingTitle"),
            DisplayText.Resource("GameHistoryRecordingStatusWaitingText"));
    }
}
