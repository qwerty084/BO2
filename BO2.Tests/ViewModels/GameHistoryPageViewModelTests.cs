using System;
using System.Globalization;
using System.Linq;
using BO2.Services;
using BO2.ViewModels;
using Xunit;

namespace BO2.Tests.ViewModels
{
    public sealed class GameHistoryPageViewModelTests
    {
        [Fact]
        public void ReplaceSavedGames_OrdersSummariesNewestFirstBySavedDate()
        {
            var viewModel = new GameHistoryPageViewModel();
            GameHistoryEntry oldest = CreateGame("oldest", 2026, 5, 8, 20, 0);
            GameHistoryEntry newest = CreateGame("newest", 2026, 5, 10, 20, 0);
            GameHistoryEntry middle = CreateGame("middle", 2026, 5, 9, 20, 0);

            viewModel.ReplaceSavedGames([oldest, newest, middle]);

            Assert.Equal(["newest", "middle", "oldest"], viewModel.SavedGames.Select(game => game.Id));
            Assert.Equal("newest", viewModel.SelectedGameSummary?.Id);
            Assert.Equal("GameHistoryTrackedGameCountFormat(3)", viewModel.TrackedGameCountText);
            Assert.True(viewModel.SavedGames[0].IsSelected);
            Assert.False(viewModel.SavedGames[1].IsSelected);
        }

        [Fact]
        public void EmptyHistory_ShowsEmptyStateAlongsideRecordingStatus()
        {
            var viewModel = new GameHistoryPageViewModel();

            Assert.Empty(viewModel.SavedGames);
            Assert.True(viewModel.IsListVisible);
            Assert.True(viewModel.IsEmptyVisible);
            Assert.Equal("GameHistoryEmptyTitle", viewModel.EmptyStateTitle);
            Assert.Equal("GameHistoryTrackedGameCountFormat(0)", viewModel.TrackedGameCountText);
            Assert.Equal("GameHistoryRecordingStatusWaitingTitle", viewModel.RecordingStatusTitle);
        }

        [Fact]
        public void SelectGame_OpensDetailAndBackReturnsToList()
        {
            var viewModel = new GameHistoryPageViewModel();
            viewModel.ReplaceSavedGames([CreateDetailedGame("town-run")]);

            viewModel.SelectGame(viewModel.SavedGames[0]);

            Assert.True(viewModel.IsDetailVisible);
            Assert.True(viewModel.IsHistoryRailVisible);
            Assert.False(viewModel.IsHistoryRailReopenButtonVisible);
            Assert.Same(viewModel.SavedGames[0], viewModel.SelectedGameSummary);
            Assert.True(viewModel.SavedGames[0].IsSelected);
            Assert.Equal("town-run", Assert.IsType<GameHistoryDetailViewModel>(viewModel.SelectedGame).Id);

            viewModel.ShowList();

            Assert.True(viewModel.IsListVisible);
            Assert.Null(viewModel.SelectedGameSummary);
            Assert.False(viewModel.SavedGames[0].IsSelected);
            Assert.Null(viewModel.SelectedGame);
        }

        [Fact]
        public void HistoryRail_CanBeHiddenAndReopenedWithoutClearingSelectedGame()
        {
            var viewModel = new GameHistoryPageViewModel();
            viewModel.ReplaceSavedGames([CreateDetailedGame("town-run")]);

            viewModel.SelectGame(viewModel.SavedGames[0]);
            viewModel.HideHistoryRail();

            Assert.True(viewModel.IsDetailVisible);
            Assert.False(viewModel.IsHistoryRailVisible);
            Assert.True(viewModel.IsHistoryRailReopenButtonVisible);
            Assert.Same(viewModel.SavedGames[0], viewModel.SelectedGameSummary);
            Assert.Equal("town-run", Assert.IsType<GameHistoryDetailViewModel>(viewModel.SelectedGame).Id);

            viewModel.ShowHistoryRail();

            Assert.True(viewModel.IsDetailVisible);
            Assert.True(viewModel.IsHistoryRailVisible);
            Assert.False(viewModel.IsHistoryRailReopenButtonVisible);
            Assert.Same(viewModel.SavedGames[0], viewModel.SelectedGameSummary);
        }

        [Fact]
        public void SelectGame_MovesSelectionMarkerBetweenSummaries()
        {
            var viewModel = new GameHistoryPageViewModel();
            viewModel.ReplaceSavedGames([CreateDetailedGame("first"), CreateDetailedGame("second")]);

            viewModel.SelectGame(viewModel.SavedGames[1]);

            Assert.False(viewModel.SavedGames[0].IsSelected);
            Assert.True(viewModel.SavedGames[1].IsSelected);
            Assert.Same(viewModel.SavedGames[1], viewModel.SelectedGameSummary);
        }

        [Fact]
        public void DetailProjection_ShowsFinalRoundStatsRoundDeltasAndMissingDurations()
        {
            var viewModel = new GameHistoryPageViewModel();
            viewModel.ReplaceSavedGames([CreateDetailedGame("town-run")]);

            viewModel.SelectGame(viewModel.SavedGames[0]);
            GameHistoryDetailViewModel detail = Assert.IsType<GameHistoryDetailViewModel>(viewModel.SelectedGame);

            Assert.Equal("Town", detail.MapNameText);
            Assert.Equal("GameHistoryFinalRoundFormat(12)", detail.FinalRoundText);
            Assert.Equal("1:02:03", detail.GameDurationText);
            Assert.Equal(12345.ToString("N0", CultureInfo.CurrentCulture), detail.FinalStats.PointsText);
            Assert.Equal("98", detail.FinalStats.KillsText);

            Assert.Equal(2, detail.Rounds.Count);
            Assert.Equal("0:45", detail.Rounds[0].DurationText);
            Assert.Equal("500", detail.Rounds[0].CumulativeStats.PointsText);
            Assert.Equal("+500", detail.Rounds[0].DeltaStats.PointsText);
            Assert.Equal(GameHistoryPageViewModel.MissingValueText, detail.Rounds[1].DurationText);
            Assert.Equal(1200.ToString("N0", CultureInfo.CurrentCulture), detail.Rounds[1].CumulativeStats.PointsText);
            Assert.Equal("+700", detail.Rounds[1].DeltaStats.PointsText);
        }

        [Fact]
        public void DetailProjection_TracksMysteryBoxAveragesWithoutInternalEventNames()
        {
            var viewModel = new GameHistoryPageViewModel();
            viewModel.ReplaceSavedGames([CreateDetailedGame("town-run")]);

            viewModel.SelectGame(viewModel.SavedGames[0]);
            GameHistoryDetailViewModel detail = Assert.IsType<GameHistoryDetailViewModel>(viewModel.SelectedGame);

            Assert.True(detail.HasBoxEvents);
            Assert.Equal(3, detail.BoxEvents.Count);
            Assert.Equal("3", detail.BoxRollCountText);
            Assert.Equal("2", detail.BoxUniqueWeaponCountText);
            Assert.Equal((3d / 12).ToString("0.#", CultureInfo.CurrentCulture), detail.BoxAverageRollsPerRoundText);
            Assert.Equal("Ray Gun", detail.BoxMostSeenWeaponText);

            Assert.Equal("Ray Gun", detail.BoxEvents[0].WeaponText);
            Assert.Equal("Ray Gun", detail.BoxEvents[1].WeaponText);
            Assert.Equal("GameHistoryBoxEventUnknownWeapon", detail.BoxEvents[2].WeaponText);

            Assert.Equal(2, detail.BoxWeaponAverages.Count);
            Assert.Equal("Ray Gun", detail.BoxWeaponAverages[0].WeaponText);
            Assert.Equal("2", detail.BoxWeaponAverages[0].RollCountText);
            Assert.Equal(2.5.ToString("0.#", CultureInfo.CurrentCulture), detail.BoxWeaponAverages[0].AverageRoundText);
            Assert.Equal((2d / 3).ToString("P0", CultureInfo.CurrentCulture), detail.BoxWeaponAverages[0].ShareText);

            Assert.Equal("GameHistoryBoxEventUnknownWeapon", detail.BoxWeaponAverages[1].WeaponText);
            Assert.Equal("1", detail.BoxWeaponAverages[1].RollCountText);
            Assert.Equal("4", detail.BoxWeaponAverages[1].AverageRoundText);
            Assert.Equal((1d / 3).ToString("P0", CultureInfo.CurrentCulture), detail.BoxWeaponAverages[1].ShareText);

            Assert.All(detail.BoxEvents, boxEvent =>
            {
                Assert.DoesNotContain("randomization_done", boxEvent.PrimaryText, StringComparison.Ordinal);
                Assert.DoesNotContain("user_grabbed_weapon", boxEvent.PrimaryText, StringComparison.Ordinal);
                Assert.DoesNotContain("ray_gun_zm", boxEvent.PrimaryText, StringComparison.Ordinal);
                Assert.DoesNotContain("zm_weap_future", boxEvent.PrimaryText, StringComparison.Ordinal);
            });
        }

        [Fact]
        public void ApplyRecordingStatus_ProjectsActiveUnavailableAndDiscardedStatesWithoutAddingEntries()
        {
            var viewModel = new GameHistoryPageViewModel();

            viewModel.ApplyRecordingStatus(GameHistoryRecordingStatus.Recording(3));
            Assert.Equal("GameHistoryRecordingStatusActiveTitle", viewModel.RecordingStatusTitle);
            Assert.Equal("GameHistoryRecordingStatusActiveRoundFormat(3)", viewModel.RecordingStatusText);

            viewModel.ApplyRecordingStatus(GameHistoryRecordingStatus.Unavailable(
                GameHistoryRecordingUnavailableReason.RequiresTown));
            Assert.Equal("GameHistoryRecordingStatusRequiresTownText", viewModel.RecordingStatusText);

            viewModel.ApplyRecordingStatus(GameHistoryRecordingStatus.Unavailable(
                GameHistoryRecordingUnavailableReason.RequiresHookBackedEventMonitor));
            Assert.Equal("GameHistoryRecordingStatusRequiresHookText", viewModel.RecordingStatusText);

            viewModel.ApplyRecordingStatus(GameHistoryRecordingStatus.Discarded(
                GameHistoryRecordingDiscardReason.SequenceGap));
            Assert.Equal("GameHistoryRecordingStatusDiscardedSequenceText", viewModel.RecordingStatusText);
            Assert.Empty(viewModel.SavedGames);

            viewModel.ApplyRecordingStatus(GameHistoryRecordingStatus.Discarded(
                GameHistoryRecordingDiscardReason.MissingRequiredStats));
            Assert.Equal("GameHistoryRecordingStatusDiscardedMissingStatsText", viewModel.RecordingStatusText);

            viewModel.ApplyRecordingStatus(GameHistoryRecordingStatus.Discarded(
                GameHistoryRecordingDiscardReason.Disconnected));
            Assert.Equal("GameHistoryRecordingStatusDiscardedConnectionEndedText", viewModel.RecordingStatusText);
        }

        private static GameHistoryEntry CreateDetailedGame(string id)
        {
            GameHistoryEntry game = CreateGame(id, 2026, 5, 10, 14, 30);
            game.FinalRound = 12;
            game.FinalStats = CreateStats(12345, 98, 2, 4, 55);
            game.GameDuration = TimeSpan.FromSeconds(3723);
            game.Rounds =
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
            ];
            game.BoxEvents =
            [
                new GameHistoryBoxEvent
                {
                    ReceivedAt = CreateLocalDate(2026, 5, 10, 14, 45),
                    RoundNumber = 2,
                    EventName = "randomization_done",
                    RawWeaponToken = "ray_gun_zm",
                    WeaponDisplayName = "Ray Gun",
                    OwnerId = 7,
                    StringValue = 100
                },
                new GameHistoryBoxEvent
                {
                    ReceivedAt = CreateLocalDate(2026, 5, 10, 14, 46),
                    RoundNumber = 2,
                    EventName = "user_grabbed_weapon",
                    RawWeaponToken = "ray_gun_zm",
                    WeaponDisplayName = "Ray Gun",
                    OwnerId = 7,
                    StringValue = 200
                },
                new GameHistoryBoxEvent
                {
                    ReceivedAt = CreateLocalDate(2026, 5, 10, 14, 47),
                    RoundNumber = 3,
                    EventName = "randomization_done",
                    RawWeaponToken = "ray_gun_zm",
                    WeaponDisplayName = "Ray Gun",
                    OwnerId = 7,
                    StringValue = 101
                },
                new GameHistoryBoxEvent
                {
                    ReceivedAt = CreateLocalDate(2026, 5, 10, 14, 48),
                    RoundNumber = 4,
                    EventName = "randomization_done",
                    RawWeaponToken = "zm_weap_future",
                    WeaponDisplayName = null,
                    OwnerId = 42,
                    StringValue = 102
                }
            ];
            return game;
        }

        private static GameHistoryEntry CreateGame(
            string id,
            int year,
            int month,
            int day,
            int hour,
            int minute)
        {
            DateTimeOffset startedAt = CreateLocalDate(year, month, day, hour, minute);
            return new GameHistoryEntry
            {
                Id = id,
                StartedAt = startedAt,
                EndedAt = startedAt.AddMinutes(30),
                MapIdentity = new GameHistoryMapIdentity("zm_transit", "town", "zm_transit_gump_town", "Town"),
                FinalRound = 5,
                FinalStats = CreateStats(1000, 20, 1, 0, 8),
                GameDuration = TimeSpan.FromMinutes(30)
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
    }
}
