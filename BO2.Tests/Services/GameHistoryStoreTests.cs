using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BO2.Services;
using Microsoft.Data.Sqlite;
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
        public async Task AppendAsync_WhenSavedGameIdAlreadyExists_ThrowsAndKeepsOriginalSummary()
        {
            var store = new GameHistoryStore(CreateDatabasePath());
            await store.AppendAsync(CreateEntry("duplicate", daysOffset: 0), CancellationToken.None);

            await Assert.ThrowsAsync<SqliteException>(
                () => store.AppendAsync(CreateEntry("duplicate", daysOffset: 1), CancellationToken.None));

            IReadOnlyList<GameHistorySummary> summaries =
                await store.LoadSummariesNewestFirstAsync(CancellationToken.None);
            GameHistorySummary summary = Assert.Single(summaries);
            Assert.Equal("duplicate", summary.Id);
            Assert.Equal(new DateTimeOffset(2026, 5, 10, 13, 2, 0, TimeSpan.Zero), summary.EndedAt);
        }

        [Fact]
        public async Task AppendAsync_StoresEntryAndRoundTimestampsAndDurationsAsInt64MillisecondsBeyondUnix32BitRange()
        {
            var store = new GameHistoryStore(CreateDatabasePath());
            DateTimeOffset startedAt = new(2040, 1, 20, 12, 0, 0, TimeSpan.Zero);
            TimeSpan gameDuration = TimeSpan.FromDays(60);
            DateTimeOffset roundStartedAt = startedAt.AddDays(10);
            TimeSpan roundDuration = TimeSpan.FromDays(30);
            GameHistoryEntry entry = CreateEntry("future", daysOffset: 0);
            entry.StartedAt = startedAt;
            entry.EndedAt = startedAt.Add(gameDuration);
            entry.GameDuration = gameDuration;
            entry.Rounds =
            [
                new GameHistoryRound
                {
                    RoundNumber = 1,
                    StartedAt = roundStartedAt,
                    EndedAt = roundStartedAt.Add(roundDuration),
                    RoundDuration = roundDuration,
                    CumulativeStats = CreateStats(500, 7, 0, 0, 3),
                    DeltaStats = CreateStats(500, 7, 0, 0, 3)
                }
            ];

            await store.AppendAsync(entry, CancellationToken.None);

            GameHistorySummary summary = Assert.Single(
                await store.LoadSummariesNewestFirstAsync(CancellationToken.None));
            Assert.Equal(startedAt, summary.StartedAt);
            Assert.Equal(startedAt.Add(gameDuration), summary.EndedAt);
            Assert.Equal(gameDuration, summary.GameDuration);

            GameHistoryEntry? loadedDetail = await store.LoadDetailByIdAsync(entry.Id, CancellationToken.None);
            Assert.NotNull(loadedDetail);
            GameHistoryRound loadedRound = Assert.Single(loadedDetail.Rounds);
            Assert.Equal(roundStartedAt, loadedRound.StartedAt);
            Assert.Equal(roundStartedAt.Add(roundDuration), loadedRound.EndedAt);
            Assert.Equal(roundDuration, loadedRound.RoundDuration);

            await using SqliteConnection connection = new(new SqliteConnectionStringBuilder
            {
                DataSource = store.DatabasePath
            }.ToString());
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    started_at_unix_time_ms,
                    ended_at_unix_time_ms,
                    game_duration_ms
                FROM game_history_entries
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", entry.Id);

            await using SqliteDataReader reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            long startedAtMilliseconds = reader.GetInt64(0);
            long endedAtMilliseconds = reader.GetInt64(1);
            long durationMilliseconds = reader.GetInt64(2);
            Assert.True(startedAtMilliseconds / 1000 > int.MaxValue);
            Assert.True(endedAtMilliseconds / 1000 > int.MaxValue);
            Assert.True(durationMilliseconds > int.MaxValue);
            Assert.Equal(startedAt.ToUnixTimeMilliseconds(), startedAtMilliseconds);
            Assert.Equal(startedAt.Add(gameDuration).ToUnixTimeMilliseconds(), endedAtMilliseconds);
            Assert.Equal((long)gameDuration.TotalMilliseconds, durationMilliseconds);

            await using SqliteCommand roundCommand = connection.CreateCommand();
            roundCommand.CommandText =
                """
                SELECT
                    started_at_unix_time_ms,
                    ended_at_unix_time_ms,
                    round_duration_ms
                FROM game_history_rounds
                WHERE game_history_entry_id = $id
                    AND round_number = 1;
                """;
            roundCommand.Parameters.AddWithValue("$id", entry.Id);

            await using SqliteDataReader roundReader = await roundCommand.ExecuteReaderAsync();
            Assert.True(await roundReader.ReadAsync());
            long roundStartedAtMilliseconds = roundReader.GetInt64(0);
            long roundEndedAtMilliseconds = roundReader.GetInt64(1);
            long roundDurationMilliseconds = roundReader.GetInt64(2);
            Assert.True(roundStartedAtMilliseconds / 1000 > int.MaxValue);
            Assert.True(roundEndedAtMilliseconds / 1000 > int.MaxValue);
            Assert.True(roundDurationMilliseconds > int.MaxValue);
            Assert.Equal(roundStartedAt.ToUnixTimeMilliseconds(), roundStartedAtMilliseconds);
            Assert.Equal(roundStartedAt.Add(roundDuration).ToUnixTimeMilliseconds(), roundEndedAtMilliseconds);
            Assert.Equal((long)roundDuration.TotalMilliseconds, roundDurationMilliseconds);
        }

        [Fact]
        public async Task LoadDetailByIdAsync_ReturnsSelectedEntryWithRoundsAndBoxEvents()
        {
            var store = new GameHistoryStore(CreateDatabasePath());
            await store.AppendAsync(CreateEntry("other", daysOffset: -1), CancellationToken.None);
            GameHistoryEntry entry = CreateEntry("saved-1", daysOffset: 0);
            entry.Rounds =
            [
                new GameHistoryRound
                {
                    RoundNumber = 2,
                    StartedAt = entry.StartedAt.AddMinutes(1),
                    EndedAt = entry.StartedAt.AddMinutes(3),
                    RoundDuration = null,
                    CumulativeStats = CreateStats(900, 12, 1, 0, 5),
                    DeltaStats = CreateStats(400, 5, 1, 0, 2)
                },
                new GameHistoryRound
                {
                    RoundNumber = 1,
                    StartedAt = entry.StartedAt,
                    EndedAt = entry.StartedAt.AddMinutes(1),
                    RoundDuration = TimeSpan.FromMinutes(1),
                    CumulativeStats = CreateStats(500, 7, 0, 0, 3),
                    DeltaStats = CreateStats(500, 7, 0, 0, 3)
                }
            ];
            entry.BoxEvents =
            [
                new GameHistoryBoxEvent
                {
                    ReceivedAt = entry.StartedAt.AddMinutes(6),
                    RoundNumber = 2,
                    EventName = "closed",
                    RawWeaponToken = null,
                    WeaponDisplayName = null,
                    OwnerId = 8,
                    StringValue = 101
                },
                new GameHistoryBoxEvent
                {
                    ReceivedAt = entry.StartedAt.AddMinutes(5),
                    RoundNumber = 1,
                    EventName = "randomization_done",
                    RawWeaponToken = "ray_gun_zm",
                    WeaponDisplayName = "Ray Gun",
                    OwnerId = 7,
                    StringValue = 100
                },
                new GameHistoryBoxEvent
                {
                    ReceivedAt = entry.StartedAt.AddMinutes(5),
                    RoundNumber = 1,
                    EventName = "user_grabbed_weapon",
                    RawWeaponToken = "m32_zm",
                    WeaponDisplayName = "War Machine",
                    OwnerId = 9,
                    StringValue = 200
                }
            ];
            await store.AppendAsync(entry, CancellationToken.None);

            GameHistoryEntry? loaded = await store.LoadDetailByIdAsync("saved-1", CancellationToken.None);

            Assert.NotNull(loaded);
            Assert.Equal("saved-1", loaded.Id);
            Assert.Equal("Town", loaded.MapIdentity.FriendlyName);
            Assert.Equal(12, loaded.FinalRound);
            Assert.Equal(12345, loaded.FinalStats.Points);
            Assert.Equal(TimeSpan.FromMinutes(62), loaded.GameDuration);
            Assert.Equal([1, 2], loaded.Rounds.Select(round => round.RoundNumber));
            Assert.Equal(entry.StartedAt, loaded.Rounds[0].StartedAt);
            Assert.Equal(entry.StartedAt.AddMinutes(1), loaded.Rounds[0].EndedAt);
            Assert.Equal(TimeSpan.FromMinutes(1), loaded.Rounds[0].RoundDuration);
            Assert.Equal(500, loaded.Rounds[0].CumulativeStats.Points);
            Assert.Equal(500, loaded.Rounds[0].DeltaStats.Points);
            Assert.Equal(entry.StartedAt.AddMinutes(1), loaded.Rounds[1].StartedAt);
            Assert.Equal(entry.StartedAt.AddMinutes(3), loaded.Rounds[1].EndedAt);
            Assert.Null(loaded.Rounds[1].RoundDuration);
            Assert.Equal(900, loaded.Rounds[1].CumulativeStats.Points);
            Assert.Equal(400, loaded.Rounds[1].DeltaStats.Points);
            Assert.Collection(
                loaded.BoxEvents,
                boxEvent =>
                {
                    Assert.Equal(entry.StartedAt.AddMinutes(5), boxEvent.ReceivedAt);
                    Assert.Equal(1, boxEvent.RoundNumber);
                    Assert.Equal("randomization_done", boxEvent.EventName);
                    Assert.Equal("ray_gun_zm", boxEvent.RawWeaponToken);
                    Assert.Equal("Ray Gun", boxEvent.WeaponDisplayName);
                    Assert.Equal((uint)7, boxEvent.OwnerId);
                    Assert.Equal((uint)100, boxEvent.StringValue);
                },
                boxEvent =>
                {
                    Assert.Equal(entry.StartedAt.AddMinutes(5), boxEvent.ReceivedAt);
                    Assert.Equal(1, boxEvent.RoundNumber);
                    Assert.Equal("user_grabbed_weapon", boxEvent.EventName);
                    Assert.Equal("m32_zm", boxEvent.RawWeaponToken);
                    Assert.Equal("War Machine", boxEvent.WeaponDisplayName);
                    Assert.Equal((uint)9, boxEvent.OwnerId);
                    Assert.Equal((uint)200, boxEvent.StringValue);
                },
                boxEvent =>
                {
                    Assert.Equal(entry.StartedAt.AddMinutes(6), boxEvent.ReceivedAt);
                    Assert.Equal(2, boxEvent.RoundNumber);
                    Assert.Equal("closed", boxEvent.EventName);
                    Assert.Null(boxEvent.RawWeaponToken);
                    Assert.Null(boxEvent.WeaponDisplayName);
                    Assert.Equal((uint)8, boxEvent.OwnerId);
                    Assert.Equal((uint)101, boxEvent.StringValue);
                });
        }

        [Fact]
        public async Task AppendAsync_WhenRoundInsertFails_RollsBackCompletedGameAppend()
        {
            var store = new GameHistoryStore(CreateDatabasePath());
            await store.LoadSummariesNewestFirstAsync(CancellationToken.None);
            await CreateFailingRoundInsertTriggerAsync(store.DatabasePath);
            GameHistoryEntry entry = CreateEntry("round-fails", daysOffset: 0);
            entry.Rounds =
            [
                new GameHistoryRound
                {
                    RoundNumber = 1,
                    StartedAt = entry.StartedAt,
                    EndedAt = entry.StartedAt.AddMinutes(1),
                    RoundDuration = TimeSpan.FromMinutes(1),
                    CumulativeStats = CreateStats(500, 7, 0, 0, 3),
                    DeltaStats = CreateStats(500, 7, 0, 0, 3)
                }
            ];

            await Assert.ThrowsAsync<SqliteException>(
                () => store.AppendAsync(entry, CancellationToken.None));

            Assert.Empty(await store.LoadSummariesNewestFirstAsync(CancellationToken.None));
            Assert.Null(await store.LoadDetailByIdAsync(entry.Id, CancellationToken.None));
        }

        [Fact]
        public async Task AppendAsync_WhenBoxEventInsertFails_RollsBackCompletedGameAppend()
        {
            var store = new GameHistoryStore(CreateDatabasePath());
            await store.LoadSummariesNewestFirstAsync(CancellationToken.None);
            await CreateFailingBoxEventInsertTriggerAsync(store.DatabasePath);
            GameHistoryEntry entry = CreateEntry("box-event-fails", daysOffset: 0);
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

            await Assert.ThrowsAsync<SqliteException>(
                () => store.AppendAsync(entry, CancellationToken.None));

            Assert.Empty(await store.LoadSummariesNewestFirstAsync(CancellationToken.None));
            Assert.Null(await store.LoadDetailByIdAsync(entry.Id, CancellationToken.None));
        }

        [Fact]
        public async Task LoadDetailByIdAsync_WhenIdIsUnknown_ReturnsNull()
        {
            var store = new GameHistoryStore(CreateDatabasePath());
            await store.AppendAsync(CreateEntry("saved-1", daysOffset: 0), CancellationToken.None);

            GameHistoryEntry? loaded = await store.LoadDetailByIdAsync("missing", CancellationToken.None);

            Assert.Null(loaded);
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

        private static async Task CreateFailingRoundInsertTriggerAsync(string databasePath)
        {
            await using SqliteConnection connection = new(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath
            }.ToString());
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TRIGGER fail_game_history_round_insert
                BEFORE INSERT ON game_history_rounds
                BEGIN
                    SELECT RAISE(FAIL, 'round insert failed');
                END;
                """;
            await command.ExecuteNonQueryAsync();
        }

        private static async Task CreateFailingBoxEventInsertTriggerAsync(string databasePath)
        {
            await using SqliteConnection connection = new(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath
            }.ToString());
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TRIGGER fail_game_history_box_event_insert
                BEFORE INSERT ON game_history_box_events
                BEGIN
                    SELECT RAISE(FAIL, 'box event insert failed');
                END;
                """;
            await command.ExecuteNonQueryAsync();
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
