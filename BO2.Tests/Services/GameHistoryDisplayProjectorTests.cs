using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameHistoryDisplayProjectorTests
    {
        public enum RecordingStatusCase
        {
            ActiveMapRound,
            ActiveRoundOnly,
            ActiveTrimmedMap,
            ActiveNoMap,
            WaitingForRoundOneMap,
            WaitingForRoundOneNoMap,
            SavePendingMap,
            SavePendingNoMap,
            SavedMap,
            SavedNoMap,
            FailedSaveMap,
            FailedSaveNoMap,
            UnavailableNotConnected,
            UnavailableRequiresSupportedMap,
            UnavailableMissingMapIdentity,
            UnavailableMissingFriendlyMapName,
            UnavailableRequiresHookBackedEventMonitor,
            DiscardedSequenceGap,
            DiscardedDroppedLifecycleData,
            DiscardedPollingFallback,
            DiscardedMissingRequiredStats,
            DiscardedDisconnected,
            DiscardedDetectedGameChanged,
            DiscardedAppClosed,
            DiscardedMissingMapIdentity,
            DiscardedUnsupportedMapIdentity,
            DiscardedMissingFriendlyMapName
        }

        public static TheoryData<RecordingStatusCase, string, string> RecordingStatusCases { get; } = new()
        {
            {
                RecordingStatusCase.ActiveMapRound,
                "GameHistoryRecordingStatusActiveTitle",
                "GameHistoryRecordingStatusActiveMapRoundFormat(Farm, 3)"
            },
            {
                RecordingStatusCase.ActiveRoundOnly,
                "GameHistoryRecordingStatusActiveTitle",
                "GameHistoryRecordingStatusActiveRoundFormat(3)"
            },
            {
                RecordingStatusCase.ActiveTrimmedMap,
                "GameHistoryRecordingStatusActiveTitle",
                "GameHistoryRecordingStatusActiveMapFormat(Farm)"
            },
            {
                RecordingStatusCase.ActiveNoMap,
                "GameHistoryRecordingStatusActiveTitle",
                "GameHistoryRecordingStatusActiveText"
            },
            {
                RecordingStatusCase.WaitingForRoundOneMap,
                "GameHistoryRecordingStatusActiveTitle",
                "GameHistoryRecordingStatusWaitingForRoundOneMapFormat(Farm)"
            },
            {
                RecordingStatusCase.WaitingForRoundOneNoMap,
                "GameHistoryRecordingStatusActiveTitle",
                "GameHistoryRecordingStatusWaitingForRoundOneText"
            },
            {
                RecordingStatusCase.SavePendingMap,
                "GameHistoryRecordingStatusSavePendingTitle",
                "GameHistoryRecordingStatusSavePendingMapFormat(Farm)"
            },
            {
                RecordingStatusCase.SavePendingNoMap,
                "GameHistoryRecordingStatusSavePendingTitle",
                "GameHistoryRecordingStatusSavePendingText"
            },
            {
                RecordingStatusCase.SavedMap,
                "GameHistoryRecordingStatusSavedTitle",
                "GameHistoryRecordingStatusSavedMapFormat(Farm)"
            },
            {
                RecordingStatusCase.SavedNoMap,
                "GameHistoryRecordingStatusSavedTitle",
                "GameHistoryRecordingStatusSavedText"
            },
            {
                RecordingStatusCase.FailedSaveMap,
                "GameHistoryRecordingStatusFailedSaveTitle",
                "GameHistoryRecordingStatusFailedSaveMapFormat(Farm)"
            },
            {
                RecordingStatusCase.FailedSaveNoMap,
                "GameHistoryRecordingStatusFailedSaveTitle",
                "GameHistoryRecordingStatusFailedSaveText"
            },
            {
                RecordingStatusCase.UnavailableNotConnected,
                "GameHistoryRecordingStatusWaitingTitle",
                "GameHistoryRecordingStatusWaitingText"
            },
            {
                RecordingStatusCase.UnavailableRequiresSupportedMap,
                "GameHistoryRecordingStatusRequiresSupportedMapTitle",
                "GameHistoryRecordingStatusRequiresSupportedMapText"
            },
            {
                RecordingStatusCase.UnavailableMissingMapIdentity,
                "GameHistoryRecordingStatusRequiresSupportedMapTitle",
                "GameHistoryRecordingStatusRequiresSupportedMapText"
            },
            {
                RecordingStatusCase.UnavailableMissingFriendlyMapName,
                "GameHistoryRecordingStatusRequiresSupportedMapTitle",
                "GameHistoryRecordingStatusRequiresSupportedMapText"
            },
            {
                RecordingStatusCase.UnavailableRequiresHookBackedEventMonitor,
                "GameHistoryRecordingStatusRequiresHookTitle",
                "GameHistoryRecordingStatusRequiresHookText"
            },
            {
                RecordingStatusCase.DiscardedSequenceGap,
                "GameHistoryRecordingStatusDiscardedTitle",
                "GameHistoryRecordingStatusDiscardedSequenceText"
            },
            {
                RecordingStatusCase.DiscardedDroppedLifecycleData,
                "GameHistoryRecordingStatusDiscardedTitle",
                "GameHistoryRecordingStatusDiscardedSequenceText"
            },
            {
                RecordingStatusCase.DiscardedPollingFallback,
                "GameHistoryRecordingStatusDiscardedTitle",
                "GameHistoryRecordingStatusDiscardedSequenceText"
            },
            {
                RecordingStatusCase.DiscardedMissingRequiredStats,
                "GameHistoryRecordingStatusDiscardedTitle",
                "GameHistoryRecordingStatusDiscardedMissingStatsText"
            },
            {
                RecordingStatusCase.DiscardedDisconnected,
                "GameHistoryRecordingStatusDiscardedTitle",
                "GameHistoryRecordingStatusDiscardedConnectionEndedText"
            },
            {
                RecordingStatusCase.DiscardedDetectedGameChanged,
                "GameHistoryRecordingStatusDiscardedTitle",
                "GameHistoryRecordingStatusDiscardedConnectionEndedText"
            },
            {
                RecordingStatusCase.DiscardedAppClosed,
                "GameHistoryRecordingStatusDiscardedTitle",
                "GameHistoryRecordingStatusDiscardedConnectionEndedText"
            },
            {
                RecordingStatusCase.DiscardedMissingMapIdentity,
                "GameHistoryRecordingStatusRequiresSupportedMapTitle",
                "GameHistoryRecordingStatusRequiresSupportedMapText"
            },
            {
                RecordingStatusCase.DiscardedUnsupportedMapIdentity,
                "GameHistoryRecordingStatusRequiresSupportedMapTitle",
                "GameHistoryRecordingStatusRequiresSupportedMapText"
            },
            {
                RecordingStatusCase.DiscardedMissingFriendlyMapName,
                "GameHistoryRecordingStatusRequiresSupportedMapTitle",
                "GameHistoryRecordingStatusRequiresSupportedMapText"
            },
        };

        [Fact]
        public void ProjectSavedSummaries_ReturnsDisplayStatesNewestFirst()
        {
            DateTimeOffset sameEndedAt = CreateLocalDate(2026, 5, 9, 20, 0);
            DateTimeOffset sameStartedAt = CreateLocalDate(2026, 5, 9, 18, 0);
            GameHistorySummary oldest = CreateSummary(
                "oldest",
                endedAt: CreateLocalDate(2026, 5, 8, 20, 0));
            GameHistorySummary newest = CreateSummary(
                "newest",
                endedAt: CreateLocalDate(2026, 5, 10, 20, 0));
            GameHistorySummary sameEndLaterStart = CreateSummary(
                "same-end-later-start",
                startedAt: CreateLocalDate(2026, 5, 9, 19, 0),
                endedAt: sameEndedAt);
            GameHistorySummary sameEndSameStartB = CreateSummary(
                "same-end-same-start-b",
                startedAt: sameStartedAt,
                endedAt: sameEndedAt);
            GameHistorySummary sameEndSameStartA = CreateSummary(
                "same-end-same-start-a",
                startedAt: sameStartedAt,
                endedAt: sameEndedAt);

            IReadOnlyList<GameHistorySummaryDisplayState> states = CreateProjector().ProjectSavedSummaries(
                [oldest, newest, sameEndSameStartB, sameEndLaterStart, sameEndSameStartA]);

            Assert.Equal(
                ["newest", "same-end-later-start", "same-end-same-start-a", "same-end-same-start-b", "oldest"],
                states.Select(state => state.Id));
        }

        [Fact]
        public void ProjectSavedSummaries_FormatsSummaryDisplayText()
        {
            CultureInfo culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.DateTimeFormat.ShortDatePattern = "M/d/yyyy";
            culture.DateTimeFormat.ShortTimePattern = "HH:mm";
            using var cultureScope = new CultureScope(culture);
            DateTimeOffset startedAt = CreateLocalDate(2026, 5, 15, 20, 0);
            GameHistorySummary summary = CreateSummary(
                "town-run",
                startedAt,
                startedAt.AddMinutes(30),
                friendlyName: "Town",
                finalRound: 12,
                finalStats: CreateStats(12345, 98, 2, 4, 55),
                gameDuration: TimeSpan.FromSeconds(3723));

            GameHistorySummaryDisplayState state = Assert.Single(CreateProjector().ProjectSavedSummaries([summary]));

            Assert.Equal("town-run", state.Id);
            Assert.Equal(summary.EndedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), state.DateText);
            Assert.Equal("5/15/2026 20:30", state.DateText);
            Assert.Equal("Town", state.MapNameText);
            Assert.Equal("GameHistoryFinalRoundFormat(12)", state.FinalRoundText);
            Assert.Equal("1:02:03", state.GameDurationText);
            Assert.Equal(12345.ToString("N0", CultureInfo.CurrentCulture), state.PointsText);
            Assert.Equal("98", state.KillsText);
            Assert.Equal("2", state.DownsText);
            Assert.Equal("4", state.RevivesText);
            Assert.Equal("55", state.HeadshotsText);
        }

        [Fact]
        public void ProjectSavedSummaries_UsesMissingTextForNullDuration()
        {
            GameHistorySummary summary = CreateSummary("town-run", gameDuration: null);

            GameHistorySummaryDisplayState state = Assert.Single(CreateProjector().ProjectSavedSummaries([summary]));

            Assert.Equal("--", state.GameDurationText);
        }

        [Fact]
        public void ProjectSavedSummaries_UsesMissingTextForNegativeDuration()
        {
            GameHistorySummary summary = CreateSummary("town-run", gameDuration: TimeSpan.FromSeconds(-1));

            GameHistorySummaryDisplayState state = Assert.Single(CreateProjector().ProjectSavedSummaries([summary]));

            Assert.Equal("--", state.GameDurationText);
        }

        [Fact]
        public void ProjectSelectedDetail_FormatsHeaderFinalStatsAndRounds()
        {
            CultureInfo culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.DateTimeFormat.ShortDatePattern = "M/d/yyyy";
            culture.DateTimeFormat.ShortTimePattern = "HH:mm";
            using var cultureScope = new CultureScope(culture);
            GameHistoryEntry game = CreateDetailedGame("town-run");

            GameHistoryDetailDisplayState state = CreateProjector().ProjectSelectedDetail(game);

            Assert.Equal("town-run", state.Id);
            Assert.Equal("Town", state.MapNameText);
            Assert.Equal("5/10/2026 15:00", state.DateText);
            Assert.Equal("GameHistoryFinalRoundFormat(12)", state.FinalRoundText);
            Assert.Equal("1:02:03", state.GameDurationText);
            AssertStats(
                new GameHistoryStatsDisplayState("12,345", "98", "2", "4", "55"),
                state.FinalStats);

            Assert.Equal([1, 2], state.Rounds.Select(static round => round.RoundNumber));
            Assert.Equal("GameHistoryRoundTitleFormat(1)", state.Rounds[0].RoundTitleText);
            Assert.Equal("0:45", state.Rounds[0].DurationText);
            AssertStats(
                new GameHistoryStatsDisplayState("500", "7", "0", "0", "3"),
                state.Rounds[0].CumulativeStats);
            AssertStats(
                new GameHistoryStatsDisplayState("+500", "+7", "0", "0", "+3"),
                state.Rounds[0].DeltaStats);
            Assert.Equal("GameHistoryRoundTitleFormat(2)", state.Rounds[1].RoundTitleText);
            Assert.Equal("--", state.Rounds[1].DurationText);
            AssertStats(
                new GameHistoryStatsDisplayState("1,200", "16", "1", "0", "8"),
                state.Rounds[1].CumulativeStats);
            AssertStats(
                new GameHistoryStatsDisplayState("+700", "+9", "+1", "0", "+5"),
                state.Rounds[1].DeltaStats);
        }

        [Fact]
        public void ProjectSelectedDetail_UsesMissingTextForNegativeRoundDuration()
        {
            GameHistoryEntry game = CreateDetailedGame("town-run");
            game.Rounds[0].RoundDuration = TimeSpan.FromSeconds(-1);

            GameHistoryDetailDisplayState state = CreateProjector().ProjectSelectedDetail(game);

            GameHistoryRoundDisplayState round = Assert.Single(
                state.Rounds,
                static candidate => candidate.RoundNumber == 2);
            Assert.Equal("--", round.DurationText);
        }

        [Theory]
        [InlineData("zm_transit", "farm", "zm_transit_gump_farm", "Farm")]
        [InlineData("zm_transit", "transit", "zm_transit_gump_transit_zclassic", "TranZit")]
        [InlineData("zm_transit", "transit", "zm_transit_gump_transit_zstandard", "Bus Depot")]
        [InlineData("zm_buried", null, "zm_buried", "Buried")]
        [InlineData("zm_highrise", null, "zm_highrise", "Die Rise")]
        [InlineData("zm_prison", null, "zm_prison", "Mob of the Dead")]
        [InlineData("zm_nuked", null, "zm_nuked", "Nuketown")]
        [InlineData("zm_tomb", null, "zm_tomb", "Origins")]
        public void ProjectSelectedDetail_UsesFriendlyMapName(
            string baseMapToken,
            string? startLocationToken,
            string internalMapToken,
            string friendlyName)
        {
            GameHistoryEntry game = CreateDetailedGame(
                "saved-run",
                startLocationToken,
                internalMapToken,
                friendlyName,
                baseMapToken);

            GameHistoryDetailDisplayState state = CreateProjector().ProjectSelectedDetail(game);

            Assert.Equal(friendlyName, state.MapNameText);
        }

        [Fact]
        public void SummaryDisplayStateContract_IncludesSummaryTextFieldsOnly()
        {
            string[] propertyNames =
            [
                .. typeof(GameHistorySummaryDisplayState)
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Select(property => property.Name)
            ];
            string[] requiredProperties =
            [
                nameof(GameHistorySummaryDisplayState.Id),
                nameof(GameHistorySummaryDisplayState.DateText),
                nameof(GameHistorySummaryDisplayState.MapNameText),
                nameof(GameHistorySummaryDisplayState.FinalRoundText),
                nameof(GameHistorySummaryDisplayState.GameDurationText),
                nameof(GameHistorySummaryDisplayState.PointsText),
                nameof(GameHistorySummaryDisplayState.KillsText),
                nameof(GameHistorySummaryDisplayState.DownsText),
                nameof(GameHistorySummaryDisplayState.RevivesText),
                nameof(GameHistorySummaryDisplayState.HeadshotsText)
            ];

            Assert.True(typeof(GameHistorySummaryDisplayState).IsSealed);
            Assert.Equal(
                requiredProperties.Order(StringComparer.Ordinal),
                propertyNames.Order(StringComparer.Ordinal));
        }

        [Fact]
        public void DetailDisplayStateContracts_IncludeDetailStatsAndRoundTextFields()
        {
            Assert.True(typeof(GameHistoryDetailDisplayState).IsSealed);
            Assert.Equal(
                new[]
                {
                    nameof(GameHistoryDetailDisplayState.Id),
                    nameof(GameHistoryDetailDisplayState.DateText),
                    nameof(GameHistoryDetailDisplayState.MapNameText),
                    nameof(GameHistoryDetailDisplayState.FinalRoundText),
                    nameof(GameHistoryDetailDisplayState.GameDurationText),
                    nameof(GameHistoryDetailDisplayState.FinalStats),
                    nameof(GameHistoryDetailDisplayState.Rounds)
                }.Order(StringComparer.Ordinal),
                GetPropertyNames<GameHistoryDetailDisplayState>());

            Assert.True(typeof(GameHistoryStatsDisplayState).IsSealed);
            Assert.Equal(
                new[]
                {
                    nameof(GameHistoryStatsDisplayState.PointsText),
                    nameof(GameHistoryStatsDisplayState.KillsText),
                    nameof(GameHistoryStatsDisplayState.DownsText),
                    nameof(GameHistoryStatsDisplayState.RevivesText),
                    nameof(GameHistoryStatsDisplayState.HeadshotsText)
                }.Order(StringComparer.Ordinal),
                GetPropertyNames<GameHistoryStatsDisplayState>());

            Assert.True(typeof(GameHistoryRoundDisplayState).IsSealed);
            Assert.Equal(
                new[]
                {
                    nameof(GameHistoryRoundDisplayState.RoundNumber),
                    nameof(GameHistoryRoundDisplayState.RoundTitleText),
                    nameof(GameHistoryRoundDisplayState.DurationText),
                    nameof(GameHistoryRoundDisplayState.CumulativeStats),
                    nameof(GameHistoryRoundDisplayState.DeltaStats)
                }.Order(StringComparer.Ordinal),
                GetPropertyNames<GameHistoryRoundDisplayState>());
        }

        [Theory]
        [MemberData(nameof(RecordingStatusCases))]
        public void ProjectRecordingStatus_ReturnsRenderedTitleAndBodyText(
            RecordingStatusCase statusCase,
            string expectedTitle,
            string expectedBodyText)
        {
            GameHistoryRecordingStatus status = CreateStatus(statusCase);
            GameHistoryRecordingStatusDisplayState state = CreateProjector().ProjectRecordingStatus(status);

            Assert.Equal(expectedTitle, state.Title);
            Assert.Equal(expectedBodyText, state.BodyText);
        }

        [Fact]
        public void DisplayStateContract_IncludesRecordingStatusTextFieldsOnly()
        {
            string[] propertyNames =
            [
                .. typeof(GameHistoryRecordingStatusDisplayState)
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Select(property => property.Name)
            ];
            string[] requiredProperties =
            [
                nameof(GameHistoryRecordingStatusDisplayState.Title),
                nameof(GameHistoryRecordingStatusDisplayState.BodyText)
            ];

            Assert.True(typeof(GameHistoryRecordingStatusDisplayState).IsSealed);
            Assert.Equal(
                requiredProperties.Order(StringComparer.Ordinal),
                propertyNames.Order(StringComparer.Ordinal));
        }

        private static GameHistoryDisplayProjector CreateProjector()
        {
            return new GameHistoryDisplayProjector();
        }

        private static GameHistorySummary CreateSummary(
            string id,
            DateTimeOffset? startedAt = null,
            DateTimeOffset? endedAt = null,
            string friendlyName = "Town",
            int finalRound = 5,
            GameHistoryStats? finalStats = null,
            TimeSpan? gameDuration = null)
        {
            DateTimeOffset effectiveStartedAt = startedAt ?? CreateLocalDate(2026, 5, 10, 14, 30);
            DateTimeOffset effectiveEndedAt = endedAt ?? effectiveStartedAt.AddMinutes(30);

            return new GameHistorySummary(
                id,
                effectiveStartedAt,
                effectiveEndedAt,
                new GameHistoryMapIdentity(
                    "zm_transit",
                    "town",
                    "zm_transit_gump_town",
                    friendlyName),
                finalRound,
                finalStats ?? CreateStats(1000, 20, 1, 0, 8),
                gameDuration);
        }

        private static GameHistoryEntry CreateDetailedGame(
            string id,
            string? startLocationToken = "town",
            string internalMapToken = "zm_transit_gump_town",
            string friendlyName = "Town",
            string baseMapToken = "zm_transit")
        {
            DateTimeOffset startedAt = CreateLocalDate(2026, 5, 10, 14, 30);
            return new GameHistoryEntry
            {
                Id = id,
                StartedAt = startedAt,
                EndedAt = startedAt.AddMinutes(30),
                MapIdentity = new GameHistoryMapIdentity(
                    baseMapToken,
                    startLocationToken,
                    internalMapToken,
                    friendlyName),
                FinalRound = 12,
                FinalStats = CreateStats(12345, 98, 2, 4, 55),
                GameDuration = TimeSpan.FromSeconds(3723),
                Rounds =
                [
                    new GameHistoryRound
                    {
                        RoundNumber = 2,
                        RoundDuration = null,
                        CumulativeStats = CreateStats(1200, 16, 1, 0, 8),
                        DeltaStats = CreateStats(700, 9, 1, 0, 5)
                    },
                    new GameHistoryRound
                    {
                        RoundNumber = 1,
                        RoundDuration = TimeSpan.FromSeconds(45),
                        CumulativeStats = CreateStats(500, 7, 0, 0, 3),
                        DeltaStats = CreateStats(500, 7, 0, 0, 3)
                    }
                ]
            };
        }

        private static GameHistoryStats CreateStats(
            int points,
            int kills,
            int downs,
            int revives,
            int headshots)
        {
            return new GameHistoryStats
            {
                Points = points,
                Kills = kills,
                Downs = downs,
                Revives = revives,
                Headshots = headshots
            };
        }

        private static DateTimeOffset CreateLocalDate(int year, int month, int day, int hour, int minute)
        {
            return new DateTimeOffset(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local));
        }

        private static IReadOnlyList<string> GetPropertyNames<T>()
        {
            return
            [
                .. typeof(T)
                    .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .Select(static property => property.Name)
                    .Order(StringComparer.Ordinal)
            ];
        }

        private static void AssertStats(
            GameHistoryStatsDisplayState expected,
            GameHistoryStatsDisplayState actual)
        {
            Assert.Equal(expected.PointsText, actual.PointsText);
            Assert.Equal(expected.KillsText, actual.KillsText);
            Assert.Equal(expected.DownsText, actual.DownsText);
            Assert.Equal(expected.RevivesText, actual.RevivesText);
            Assert.Equal(expected.HeadshotsText, actual.HeadshotsText);
        }

        private static GameHistoryRecordingStatus CreateStatus(RecordingStatusCase statusCase)
        {
            return statusCase switch
            {
                RecordingStatusCase.ActiveMapRound => GameHistoryRecordingStatus.Recording(3, "Farm"),
                RecordingStatusCase.ActiveRoundOnly => GameHistoryRecordingStatus.Recording(3),
                RecordingStatusCase.ActiveTrimmedMap => GameHistoryRecordingStatus.Active(" Farm "),
                RecordingStatusCase.ActiveNoMap => GameHistoryRecordingStatus.Active(null),
                RecordingStatusCase.WaitingForRoundOneMap => GameHistoryRecordingStatus.WaitingForRoundOne("Farm"),
                RecordingStatusCase.WaitingForRoundOneNoMap => GameHistoryRecordingStatus.WaitingForRoundOne(),
                RecordingStatusCase.SavePendingMap => GameHistoryRecordingStatus.SavePending("farm-run", "Farm"),
                RecordingStatusCase.SavePendingNoMap => GameHistoryRecordingStatus.SavePending("farm-run"),
                RecordingStatusCase.SavedMap => GameHistoryRecordingStatus.Saved("farm-run", "Farm"),
                RecordingStatusCase.SavedNoMap => GameHistoryRecordingStatus.Saved("farm-run"),
                RecordingStatusCase.FailedSaveMap => GameHistoryRecordingStatus.FailedSave(
                    "farm-run",
                    "Farm",
                    "database locked"),
                RecordingStatusCase.FailedSaveNoMap => GameHistoryRecordingStatus.FailedSave("farm-run"),
                RecordingStatusCase.UnavailableNotConnected => GameHistoryRecordingStatus.Unavailable(
                    GameHistoryRecordingUnavailableReason.NotConnected),
                RecordingStatusCase.UnavailableRequiresSupportedMap => GameHistoryRecordingStatus.Unavailable(
                    GameHistoryRecordingUnavailableReason.RequiresSupportedMap),
                RecordingStatusCase.UnavailableMissingMapIdentity => GameHistoryRecordingStatus.Unavailable(
                    GameHistoryRecordingUnavailableReason.MissingMapIdentity),
                RecordingStatusCase.UnavailableMissingFriendlyMapName => GameHistoryRecordingStatus.Unavailable(
                    GameHistoryRecordingUnavailableReason.MissingFriendlyMapName),
                RecordingStatusCase.UnavailableRequiresHookBackedEventMonitor => GameHistoryRecordingStatus.Unavailable(
                    GameHistoryRecordingUnavailableReason.RequiresHookBackedEventMonitor),
                RecordingStatusCase.DiscardedSequenceGap => GameHistoryRecordingStatus.Discarded(
                    GameHistoryRecordingDiscardReason.SequenceGap),
                RecordingStatusCase.DiscardedDroppedLifecycleData => GameHistoryRecordingStatus.Discarded(
                    GameHistoryRecordingDiscardReason.DroppedLifecycleData),
                RecordingStatusCase.DiscardedPollingFallback => GameHistoryRecordingStatus.Discarded(
                    GameHistoryRecordingDiscardReason.PollingFallback),
                RecordingStatusCase.DiscardedMissingRequiredStats => GameHistoryRecordingStatus.Discarded(
                    GameHistoryRecordingDiscardReason.MissingRequiredStats),
                RecordingStatusCase.DiscardedDisconnected => GameHistoryRecordingStatus.Discarded(
                    GameHistoryRecordingDiscardReason.Disconnected),
                RecordingStatusCase.DiscardedDetectedGameChanged => GameHistoryRecordingStatus.Discarded(
                    GameHistoryRecordingDiscardReason.DetectedGameChanged),
                RecordingStatusCase.DiscardedAppClosed => GameHistoryRecordingStatus.Discarded(
                    GameHistoryRecordingDiscardReason.AppClosed),
                RecordingStatusCase.DiscardedMissingMapIdentity => GameHistoryRecordingStatus.Discarded(
                    GameHistoryRecordingDiscardReason.MissingMapIdentity),
                RecordingStatusCase.DiscardedUnsupportedMapIdentity => GameHistoryRecordingStatus.Discarded(
                    GameHistoryRecordingDiscardReason.UnsupportedMapIdentity),
                RecordingStatusCase.DiscardedMissingFriendlyMapName => GameHistoryRecordingStatus.Discarded(
                    GameHistoryRecordingDiscardReason.MissingFriendlyMapName),
                _ => throw new ArgumentOutOfRangeException(nameof(statusCase), statusCase, null)
            };
        }

        private sealed class CultureScope : IDisposable
        {
            private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;

            public CultureScope(CultureInfo culture)
            {
                CultureInfo.CurrentCulture = culture;
            }

            public void Dispose()
            {
                CultureInfo.CurrentCulture = _originalCulture;
            }
        }
    }
}
