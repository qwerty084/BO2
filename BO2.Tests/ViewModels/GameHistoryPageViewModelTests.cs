using System;
using System.Collections.Generic;
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
        public void ReplaceSavedGames_PopulatesListStateWithoutSelectingGame()
        {
            var viewModel = new GameHistoryPageViewModel();
            GameHistoryEntry game = CreateGame("town-run", 2026, 5, 8, 20, 0);

            viewModel.ReplaceSavedGames([game]);

            Assert.Equal("town-run", Assert.Single(viewModel.SavedGames).Id);
            Assert.Null(viewModel.SelectedGameSummary);
            Assert.Equal("GameHistoryTrackedGameCountFormat(1)", viewModel.TrackedGameCountText);
            Assert.False(viewModel.SavedGames[0].IsSelected);
            Assert.True(viewModel.IsListVisible);
            Assert.False(viewModel.IsDetailVisible);
        }

        [Fact]
        public void EmptyHistory_ShowsEmptyStateAlongsideRecordingStatus()
        {
            var viewModel = new GameHistoryPageViewModel();

            Assert.Empty(viewModel.SavedGames);
            Assert.True(viewModel.IsListVisible);
            Assert.True(viewModel.IsEmptyVisible);
            Assert.False(viewModel.IsSummaryLoadErrorVisible);
            Assert.False(viewModel.IsSavedGamesListVisible);
            Assert.Equal("GameHistoryEmptyTitle", viewModel.EmptyStateTitle);
            Assert.Equal("GameHistoryTrackedGameCountFormat(0)", viewModel.TrackedGameCountText);
            Assert.Equal("GameHistoryRecordingStatusWaitingTitle", viewModel.RecordingStatusTitle);
        }

        [Fact]
        public void ShowSummaryLoadError_ShowsErrorInsteadOfEmptyState()
        {
            var viewModel = new GameHistoryPageViewModel();

            viewModel.ShowSummaryLoadError("database locked");

            Assert.Empty(viewModel.SavedGames);
            Assert.True(viewModel.IsListVisible);
            Assert.False(viewModel.IsEmptyVisible);
            Assert.False(viewModel.IsSavedGamesListVisible);
            Assert.True(viewModel.IsSummaryLoadErrorVisible);
            Assert.Equal("GameHistoryLoadErrorTitle", viewModel.SummaryLoadErrorTitle);
            Assert.Equal("database locked", viewModel.SummaryLoadErrorText);
        }

        [Fact]
        public void ReplaceSummaries_ClearsSummaryLoadError()
        {
            var viewModel = new GameHistoryPageViewModel();
            viewModel.ShowSummaryLoadError("database locked");

            viewModel.ReplaceSummaries([]);

            Assert.False(viewModel.IsSummaryLoadErrorVisible);
            Assert.True(viewModel.IsEmptyVisible);
            Assert.Equal(string.Empty, viewModel.SummaryLoadErrorText);
        }

        [Fact]
        public void SelectGame_OpensDetailAndBackReturnsToList()
        {
            var viewModel = new GameHistoryPageViewModel();
            GameHistoryEntry game = CreateDetailedGame("town-run");
            string? requestedDetailId = null;
            viewModel.SelectedGameDetailRequested += (_, args) => requestedDetailId = args.Id;
            viewModel.ReplaceSavedGames([game]);

            viewModel.SelectGame(viewModel.SavedGames[0]);

            Assert.True(viewModel.IsDetailVisible);
            Assert.True(viewModel.IsHistoryRailVisible);
            Assert.False(viewModel.IsHistoryRailReopenButtonVisible);
            Assert.Same(viewModel.SavedGames[0], viewModel.SelectedGameSummary);
            Assert.True(viewModel.SavedGames[0].IsSelected);
            Assert.True(viewModel.IsSelectedGameDetailLoadingVisible);
            Assert.False(viewModel.IsSelectedGameDetailContentVisible);
            Assert.False(viewModel.IsSelectedGameDetailErrorVisible);
            Assert.Null(viewModel.SelectedGame);
            Assert.Equal("town-run", requestedDetailId);

            viewModel.ShowSelectedGameDetail(game);

            Assert.False(viewModel.IsSelectedGameDetailLoadingVisible);
            Assert.True(viewModel.IsSelectedGameDetailContentVisible);
            Assert.Equal("town-run", Assert.IsType<GameHistoryDetailViewModel>(viewModel.SelectedGame).Id);

            viewModel.ShowList();

            Assert.True(viewModel.IsListVisible);
            Assert.Null(viewModel.SelectedGameSummary);
            Assert.False(viewModel.SavedGames[0].IsSelected);
            Assert.Null(viewModel.SelectedGame);
            Assert.False(viewModel.IsSelectedGameDetailLoadingVisible);
            Assert.False(viewModel.IsSelectedGameDetailErrorVisible);
        }

        [Fact]
        public void HistoryRail_CanBeHiddenAndReopenedWithoutClearingSelectedGame()
        {
            var viewModel = new GameHistoryPageViewModel();
            GameHistoryEntry game = CreateDetailedGame("town-run");
            viewModel.ReplaceSavedGames([game]);

            viewModel.SelectGame(viewModel.SavedGames[0]);
            viewModel.ShowSelectedGameDetail(game);
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
            Assert.True(viewModel.IsSelectedGameDetailLoadingVisible);
        }

        [Fact]
        public void ReplaceSavedGames_PreservesSelectedSummaryWhenStillPresent()
        {
            var viewModel = new GameHistoryPageViewModel();
            GameHistoryEntry first = CreateDetailedGame("first");
            GameHistoryEntry second = CreateDetailedGame("second");
            viewModel.ReplaceSavedGames([first, second]);
            viewModel.SelectGame(viewModel.SavedGames.Single(game => game.Id == "second"));
            viewModel.ShowSelectedGameDetail(second);

            viewModel.ReplaceSavedGames([CreateDetailedGame("second"), CreateDetailedGame("third")]);

            Assert.Equal("second", viewModel.SelectedGameSummary?.Id);
            Assert.True(viewModel.SavedGames.Single(game => game.Id == "second").IsSelected);
            Assert.False(viewModel.SavedGames.Single(game => game.Id == "third").IsSelected);
            Assert.Equal("second", Assert.IsType<GameHistoryDetailViewModel>(viewModel.SelectedGame).Id);
            Assert.True(viewModel.IsDetailVisible);
            Assert.True(viewModel.IsSelectedGameDetailContentVisible);
        }

        [Fact]
        public void SelectGameById_SelectsSummaryAndRequestsDetailLoad()
        {
            var viewModel = new GameHistoryPageViewModel();
            string? requestedDetailId = null;
            viewModel.SelectedGameDetailRequested += (_, args) => requestedDetailId = args.Id;
            viewModel.ReplaceSavedGames([CreateDetailedGame("first"), CreateDetailedGame("second")]);

            viewModel.SelectGameById("second");

            Assert.Equal("second", viewModel.SelectedGameSummary?.Id);
            Assert.True(viewModel.SavedGames.Single(game => game.Id == "second").IsSelected);
            Assert.True(viewModel.IsSelectedGameDetailLoadingVisible);
            Assert.Equal("second", requestedDetailId);
        }

        [Fact]
        public void ShowSelectedGameDetailError_ClearsStaleDetail()
        {
            var viewModel = new GameHistoryPageViewModel();
            GameHistoryEntry first = CreateDetailedGame("first");
            GameHistoryEntry second = CreateDetailedGame("second");
            viewModel.ReplaceSavedGames([first, second]);
            SelectAndLoad(viewModel, first);

            viewModel.SelectGame(viewModel.SavedGames.Single(game => game.Id == "second"));
            viewModel.ShowSelectedGameDetailError("database locked");

            Assert.Equal("second", viewModel.SelectedGameSummary?.Id);
            Assert.Null(viewModel.SelectedGame);
            Assert.False(viewModel.IsSelectedGameDetailLoadingVisible);
            Assert.False(viewModel.IsSelectedGameDetailContentVisible);
            Assert.True(viewModel.IsSelectedGameDetailErrorVisible);
            Assert.Equal("database locked", viewModel.SelectedGameDetailErrorText);
        }

        [Fact]
        public void ShowSelectedGameDetail_IgnoresStaleLoadedDetailForPreviousSelection()
        {
            var viewModel = new GameHistoryPageViewModel();
            GameHistoryEntry first = CreateDetailedGame("first");
            GameHistoryEntry second = CreateDetailedGame("second");
            viewModel.ReplaceSavedGames([first, second]);

            viewModel.SelectGame(viewModel.SavedGames.Single(game => game.Id == "second"));
            viewModel.ShowSelectedGameDetail(first);

            Assert.Equal("second", viewModel.SelectedGameSummary?.Id);
            Assert.Null(viewModel.SelectedGame);
            Assert.True(viewModel.IsSelectedGameDetailLoadingVisible);
        }

        [Fact]
        public void DetailProjection_ShowsFinalRoundStatsRoundDeltasAndMissingDurations()
        {
            var viewModel = new GameHistoryPageViewModel();
            GameHistoryEntry game = CreateDetailedGame("town-run");
            viewModel.ReplaceSavedGames([game]);

            GameHistoryDetailViewModel detail = SelectAndLoad(viewModel, game);

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
        public void DetailDates_UseCurrentCultureShortDateAndTimeFormat()
        {
            CultureInfo culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            culture.DateTimeFormat.ShortDatePattern = "M/d/yyyy";
            culture.DateTimeFormat.ShortTimePattern = "HH:mm";
            using var cultureScope = new CultureScope(culture);
            var viewModel = new GameHistoryPageViewModel();

            GameHistoryEntry game = CreateGame("town-run", 2026, 5, 15, 20, 0);
            viewModel.ReplaceSavedGames([game]);
            GameHistoryDetailViewModel detail = SelectAndLoad(viewModel, game);

            string expectedDateText = game.EndedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            Assert.Equal("5/15/2026 20:30", expectedDateText);
            Assert.Equal(expectedDateText, detail.DateText);
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
        public void DetailProjection_ShowsMapName(
            string baseMapToken,
            string? startLocationToken,
            string internalMapToken,
            string friendlyName)
        {
            var viewModel = new GameHistoryPageViewModel();
            GameHistoryEntry game = CreateDetailedGame(
                "saved-run",
                startLocationToken,
                internalMapToken,
                friendlyName,
                baseMapToken);
            viewModel.ReplaceSavedGames([game]);

            GameHistoryDetailViewModel detail = SelectAndLoad(viewModel, game);
            Assert.Equal(friendlyName, detail.MapNameText);
        }

        [Fact]
        public void DetailProjection_TracksMysteryBoxAveragesWithoutInternalEventNames()
        {
            var viewModel = new GameHistoryPageViewModel();
            GameHistoryEntry game = CreateDetailedGame("town-run");
            viewModel.ReplaceSavedGames([game]);

            GameHistoryDetailViewModel detail = SelectAndLoad(viewModel, game);

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
        public void DetailProjection_OrdersBoxEventsByReceivedTimeAndPreservesEqualTimestampOrder()
        {
            var viewModel = new GameHistoryPageViewModel();
            GameHistoryEntry game = CreateDetailedGame("town-run");
            DateTimeOffset sameReceivedAt = CreateLocalDate(2026, 5, 10, 14, 45);
            game.BoxEvents =
            [
                new GameHistoryBoxEvent
                {
                    ReceivedAt = sameReceivedAt.AddMinutes(1),
                    RoundNumber = 2,
                    EventName = "randomization_done",
                    RawWeaponToken = "galil_zm",
                    WeaponDisplayName = "Galil",
                    OwnerId = 7,
                    StringValue = 300
                },
                new GameHistoryBoxEvent
                {
                    ReceivedAt = sameReceivedAt,
                    RoundNumber = 2,
                    EventName = "randomization_done",
                    RawWeaponToken = "ray_gun_zm",
                    WeaponDisplayName = "Ray Gun",
                    OwnerId = 7,
                    StringValue = 100
                },
                new GameHistoryBoxEvent
                {
                    ReceivedAt = sameReceivedAt,
                    RoundNumber = 2,
                    EventName = "randomization_done",
                    RawWeaponToken = "m32_zm",
                    WeaponDisplayName = "War Machine",
                    OwnerId = 7,
                    StringValue = 200
                }
            ];
            viewModel.ReplaceSavedGames([game]);

            GameHistoryDetailViewModel detail = SelectAndLoad(viewModel, game);

            Assert.Equal(["Ray Gun", "War Machine", "Galil"], detail.BoxEvents.Select(boxEvent => boxEvent.WeaponText));
        }

        [Fact]
        public void ApplyRecordingStatus_AppliesProjectedStateWithoutAddingEntries()
        {
            var viewModel = new GameHistoryPageViewModel();
            GameHistoryRecordingStatus status = GameHistoryRecordingStatus.Recording(3, "Farm");
            GameHistoryRecordingStatusDisplayState expectedState =
                new GameHistoryDisplayProjector().ProjectRecordingStatus(status);
            List<string?> changedProperties = [];
            viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

            viewModel.ApplyRecordingStatus(status);

            Assert.Equal(expectedState.Title, viewModel.RecordingStatusTitle);
            Assert.Equal(expectedState.BodyText, viewModel.RecordingStatusText);
            Assert.Empty(viewModel.SavedGames);
            Assert.Contains(nameof(GameHistoryPageViewModel.RecordingStatusTitle), changedProperties);
            Assert.Contains(nameof(GameHistoryPageViewModel.RecordingStatusText), changedProperties);
            Assert.DoesNotContain(nameof(GameHistoryPageViewModel.IsListVisible), changedProperties);
            Assert.DoesNotContain(nameof(GameHistoryPageViewModel.IsDetailVisible), changedProperties);
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
        public void ApplySnapshot_WhenSupportedMapIsReady_ShowsMapRecordingStatus(
            string baseMapToken,
            string? startLocationToken,
            string internalMapToken,
            string friendlyName)
        {
            var viewModel = new GameHistoryPageViewModel();
            DetectedGame detectedGame = CreateDetectedGame();

            viewModel.ApplySnapshot(CreateConnectedSnapshot(
                detectedGame,
                GameMapIdentityReadResult.SupportedMap(
                    detectedGame,
                    new GameMapIdentity(baseMapToken, startLocationToken, internalMapToken, friendlyName))));

            GameHistoryRecordingStatusDisplayState expectedState = new GameHistoryDisplayProjector()
                .ProjectRecordingStatus(GameHistoryRecordingStatus.WaitingForRoundOne(friendlyName));
            Assert.Equal(expectedState.Title, viewModel.RecordingStatusTitle);
            Assert.Equal(expectedState.BodyText, viewModel.RecordingStatusText);
        }

        private static GameHistoryDetailViewModel SelectAndLoad(
            GameHistoryPageViewModel viewModel,
            GameHistoryEntry game)
        {
            GameHistorySummaryViewModel summary = viewModel.SavedGames.Single(
                savedGame => string.Equals(savedGame.Id, game.Id, StringComparison.Ordinal));

            viewModel.SelectGame(summary);
            Assert.True(viewModel.IsSelectedGameDetailLoadingVisible);
            viewModel.ShowSelectedGameDetail(game);
            Assert.True(viewModel.IsSelectedGameDetailContentVisible);
            return Assert.IsType<GameHistoryDetailViewModel>(viewModel.SelectedGame);
        }

        private static GameHistoryEntry CreateDetailedGame(
            string id,
            string? startLocationToken = "town",
            string internalMapToken = "zm_transit_gump_town",
            string friendlyName = "Town",
            string baseMapToken = "zm_transit")
        {
            GameHistoryEntry game = CreateGame(
                id,
                2026,
                5,
                10,
                14,
                30,
                startLocationToken,
                internalMapToken,
                friendlyName,
                baseMapToken);
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
            int minute,
            string? startLocationToken = "town",
            string internalMapToken = "zm_transit_gump_town",
            string friendlyName = "Town",
            string baseMapToken = "zm_transit")
        {
            DateTimeOffset startedAt = CreateLocalDate(year, month, day, hour, minute);
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
                FinalRound = 5,
                FinalStats = CreateStats(1000, 20, 1, 0, 8),
                GameDuration = TimeSpan.FromMinutes(30)
            };
        }

        private static GameConnectionSnapshot CreateConnectedSnapshot(
            DetectedGame detectedGame,
            GameMapIdentityReadResult? mapIdentityResult)
        {
            return new GameConnectionSnapshot(
                detectedGame,
                GameConnectionPhase.Connected,
                null,
                new GameConnectionEventMonitorSummary(
                    GameConnectionEventMonitorState.Ready,
                    new GameEventMonitorStatus(GameCompatibilityState.Compatible, 0, 0, 1, [])),
                null,
                GameConnectionCommandAvailability.Hidden,
                GameConnectionCommandAvailability.VisibleEnabled,
                mapIdentityResult);
        }

        private static DetectedGame CreateDetectedGame()
        {
            return new DetectedGame(
                GameVariant.SteamZombies,
                "Steam Zombies",
                "t6zm",
                1001,
                PlayerStatAddressMap.SteamZombies,
                null);
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
