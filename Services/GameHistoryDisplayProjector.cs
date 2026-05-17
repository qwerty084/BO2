using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BO2.Services
{
    internal sealed class GameHistoryDisplayProjector
    {
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
                return _renderer.Render(GameConnectionDisplayTextProjector.EmptyValueText);
            }

            return GameTimerDurationFormatter.Format(value);
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

    internal sealed record GameHistoryRecordingStatusDisplayState(string Title, string BodyText);

    internal sealed record GameHistoryRecordingStatusDisplayProjection(DisplayText Title, DisplayText BodyText)
    {
        public static GameHistoryRecordingStatusDisplayProjection Waiting { get; } = new(
            DisplayText.Resource("GameHistoryRecordingStatusWaitingTitle"),
            DisplayText.Resource("GameHistoryRecordingStatusWaitingText"));
    }
}
