using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameHistoryStoreTests
    {
        [Fact]
        public async Task LoadSummariesNewestFirstAsync_WhenDatabaseIsMissing_CreatesSchemaAndReturnsEmpty()
        {
            var store = new GameHistoryStore(CreateDatabasePath());

            IReadOnlyList<GameHistorySummary> summaries =
                await store.LoadSummariesNewestFirstAsync(CancellationToken.None);

            Assert.Empty(summaries);
            Assert.True(File.Exists(store.DatabasePath));
            Assert.Equal(GameHistoryStore.CurrentSchemaVersion, await store.GetSchemaVersionAsync(CancellationToken.None));
        }

        [Fact]
        public async Task LoadSummariesNewestFirstAsync_IgnoresExistingJsonHistory()
        {
            string databasePath = CreateDatabasePath();
            string legacyJsonPath = Path.Join(Path.GetDirectoryName(databasePath), "game-history.json");
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            File.WriteAllText(
                legacyJsonPath,
                """
                {
                  "Version": 1,
                  "Entries": [
                    {
                      "Id": "legacy-json-run",
                      "StartedAt": "2026-05-10T12:00:00+00:00",
                      "EndedAt": "2026-05-10T13:02:00+00:00"
                    }
                  ]
                }
                """);
            var store = new GameHistoryStore(databasePath);

            IReadOnlyList<GameHistorySummary> summaries =
                await store.LoadSummariesNewestFirstAsync(CancellationToken.None);

            Assert.Empty(summaries);
            Assert.True(File.Exists(legacyJsonPath));
        }

        [Fact]
        public async Task LoadSummariesNewestFirstAsync_ReturnsSQLiteSummariesNewestFirst()
        {
            var store = new GameHistoryStore(CreateDatabasePath());
            await store.AppendAsync(CreateEntry("older", daysOffset: -2), CancellationToken.None);
            await store.AppendAsync(CreateEntry("newer", daysOffset: 0), CancellationToken.None);
            await store.AppendAsync(CreateEntry("middle", daysOffset: -1), CancellationToken.None);

            IReadOnlyList<GameHistorySummary> summaries =
                await store.LoadSummariesNewestFirstAsync(CancellationToken.None);

            Assert.Equal(["newer", "middle", "older"], summaries.Select(summary => summary.Id));
            Assert.Equal("Town", summaries[0].MapIdentity.FriendlyName);
            Assert.Equal(12, summaries[0].FinalRound);
            Assert.Equal(12345, summaries[0].FinalStats.Points);
            Assert.Equal(TimeSpan.FromMinutes(62), summaries[0].GameDuration);
        }

        [Fact]
        public void SaveAndLoad_PreservesSummaryRoundsAndBoxEventsInSQLite()
        {
            var store = new GameHistoryStore(CreateDatabasePath());
            GameHistoryEntry entry = CreateEntry("saved-1", daysOffset: 0);
            entry.Rounds =
            [
                new GameHistoryRound
                {
                    RoundNumber = 1,
                    StartedAt = entry.StartedAt,
                    EndedAt = entry.StartedAt.AddSeconds(45),
                    RoundDuration = TimeSpan.FromSeconds(45),
                    CumulativeStats = CreateStats(500, 7, 0, 0, 3),
                    DeltaStats = CreateStats(500, 7, 0, 0, 3)
                }
            ];
            entry.BoxEvents =
            [
                new GameHistoryBoxEvent
                {
                    ReceivedAt = entry.StartedAt.AddMinutes(5),
                    RoundNumber = 1,
                    EventName = "randomization_done",
                    RawWeaponToken = "ray_gun_zm",
                    WeaponDisplayName = "Ray Gun",
                    OwnerId = 7,
                    StringValue = 100
                }
            ];

            store.Save(new GameHistoryDocument { Entries = [entry] });
            GameHistoryDocument loaded = store.Load();

            GameHistoryEntry loadedEntry = Assert.Single(loaded.Entries);
            Assert.Equal("saved-1", loadedEntry.Id);
            Assert.Equal("Town", loadedEntry.MapIdentity.FriendlyName);
            Assert.Equal(12, loadedEntry.FinalRound);
            Assert.Equal(12345, loadedEntry.FinalStats.Points);
            Assert.Equal(TimeSpan.FromMinutes(62), loadedEntry.GameDuration);
            GameHistoryRound round = Assert.Single(loadedEntry.Rounds);
            Assert.Equal(1, round.RoundNumber);
            Assert.Equal(TimeSpan.FromSeconds(45), round.RoundDuration);
            Assert.Equal(500, round.DeltaStats.Points);
            GameHistoryBoxEvent boxEvent = Assert.Single(loadedEntry.BoxEvents);
            Assert.Equal("ray_gun_zm", boxEvent.RawWeaponToken);
            Assert.Equal("Ray Gun", boxEvent.WeaponDisplayName);
            Assert.Equal((uint)7, boxEvent.OwnerId);
        }

        [Fact]
        public async Task LoadSummariesNewestFirstAsync_WhenTokenIsCanceled_Throws()
        {
            var store = new GameHistoryStore(CreateDatabasePath());
            using var cancellationTokenSource = new CancellationTokenSource();
            cancellationTokenSource.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => store.LoadSummariesNewestFirstAsync(cancellationTokenSource.Token));
        }

        [Fact]
        public async Task LoadSummariesNewestFirstAsync_WhenDatabaseCannotOpen_Throws()
        {
            string databasePath = CreateDatabasePath();
            Directory.CreateDirectory(databasePath);
            var store = new GameHistoryStore(databasePath);

            await Assert.ThrowsAnyAsync<Exception>(
                () => store.LoadSummariesNewestFirstAsync(CancellationToken.None));
        }

        private static GameHistoryEntry CreateEntry(string id, int daysOffset)
        {
            DateTimeOffset startedAt = new(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);
            startedAt = startedAt.AddDays(daysOffset);
            return new GameHistoryEntry
            {
                Id = id,
                StartedAt = startedAt,
                EndedAt = startedAt.AddMinutes(62),
                MapIdentity = new GameHistoryMapIdentity("zm_transit", "town", "zm_transit_gump_town", "Town"),
                FinalRound = 12,
                FinalStats = CreateStats(12345, 98, 2, 4, 55),
                GameDuration = TimeSpan.FromMinutes(62)
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

        private static string CreateDatabasePath()
        {
            return Path.GetFullPath(Path.Join(
                Path.GetTempPath(),
                "BO2.Tests",
                Guid.NewGuid().ToString("N"),
                "game-history.sqlite"));
        }
    }
}
