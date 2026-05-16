using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace BO2.Services
{
    internal sealed class GameHistoryStore(string databasePath) : IDisposable
    {
        public const int CurrentSchemaVersion = 1;

        private readonly string _databasePath = Path.GetFullPath(databasePath);
        private readonly SemaphoreSlim _schemaInitializationSemaphore = new(1, 1);
        private bool _schemaInitialized;

        internal string DatabasePath => _databasePath;

        public static GameHistoryStore CreateDefault()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string databasePath = Path.GetFullPath(Path.Join(appData, "BO2", "game-history.sqlite"));
            return new GameHistoryStore(databasePath);
        }

        public void Dispose()
        {
            _schemaInitializationSemaphore.Dispose();
        }

        public void Append(GameHistoryEntry entry)
        {
            AppendAsync(entry, CancellationToken.None).GetAwaiter().GetResult();
        }

        public IReadOnlyList<GameHistorySummary> LoadSummariesNewestFirst()
        {
            return LoadSummariesNewestFirstAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public GameHistoryEntry? LoadDetailById(string id)
        {
            return LoadDetailByIdAsync(id, CancellationToken.None).GetAwaiter().GetResult();
        }

        public async Task<IReadOnlyList<GameHistorySummary>> LoadSummariesNewestFirstAsync(
            CancellationToken cancellationToken)
        {
            await EnsureInitializedAsync(cancellationToken);

            using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    id,
                    started_at_unix_time_ms,
                    ended_at_unix_time_ms,
                    base_map_token,
                    start_location_token,
                    internal_map_token,
                    friendly_name,
                    final_round,
                    final_points,
                    final_kills,
                    final_downs,
                    final_revives,
                    final_headshots,
                    game_duration_ms
                FROM game_history_entries
                ORDER BY ended_at_unix_time_ms DESC, started_at_unix_time_ms DESC, id ASC;
                """;

            List<GameHistorySummary> summaries = [];
            using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                summaries.Add(ReadSummary(reader));
            }

            return summaries;
        }

        public async Task<GameHistoryEntry?> LoadDetailByIdAsync(string id, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);

            await EnsureInitializedAsync(cancellationToken);

            using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    id,
                    started_at_unix_time_ms,
                    ended_at_unix_time_ms,
                    base_map_token,
                    start_location_token,
                    internal_map_token,
                    friendly_name,
                    final_round,
                    final_points,
                    final_kills,
                    final_downs,
                    final_revives,
                    final_headshots,
                    game_duration_ms
                FROM game_history_entries
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", id);

            GameHistorySummary summary;
            using (SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return null;
                }

                summary = ReadSummary(reader);
            }

            GameHistoryEntry entry = CreateEntry(summary);
            entry.Rounds = await LoadRoundsAsync(connection, entry.Id, cancellationToken);
            entry.BoxEvents = await LoadBoxEventsAsync(connection, entry.Id, cancellationToken);
            return entry;
        }

        public async Task AppendAsync(GameHistoryEntry entry, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(entry);

            await EnsureInitializedAsync(cancellationToken);
            using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            using SqliteTransaction transaction = connection.BeginTransaction();
            try
            {
                await InsertEntryAsync(connection, transaction, entry, cancellationToken);
                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        internal async Task<int> GetSchemaVersionAsync(CancellationToken cancellationToken)
        {
            await EnsureInitializedAsync(cancellationToken);
            using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
            return await ReadSchemaVersionAsync(connection, cancellationToken);
        }

        private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            if (_schemaInitialized)
            {
                return;
            }

            await _schemaInitializationSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (_schemaInitialized)
                {
                    return;
                }

                using SqliteConnection connection = await OpenConnectionAsync(cancellationToken);
                await InitializeSchemaAsync(connection, cancellationToken);
                _schemaInitialized = true;
            }
            finally
            {
                _schemaInitializationSemaphore.Release();
            }
        }

        private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
        {
            string? directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            SqliteConnectionStringBuilder builder = new()
            {
                DataSource = _databasePath
            };
            SqliteConnection connection = new(builder.ToString());
            try
            {
                await connection.OpenAsync(cancellationToken);
                await ExecuteNonQueryAsync(connection, null, "PRAGMA foreign_keys = ON;", cancellationToken);
                return connection;
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }

        private static async Task InitializeSchemaAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
        {
            await ExecuteNonQueryAsync(
                connection,
                null,
                """
                CREATE TABLE IF NOT EXISTS game_history_schema (
                    id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
                    version INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS game_history_entries (
                    id TEXT NOT NULL PRIMARY KEY,
                    started_at_unix_time_ms INTEGER NOT NULL,
                    ended_at_unix_time_ms INTEGER NOT NULL,
                    base_map_token TEXT NOT NULL,
                    start_location_token TEXT NULL,
                    internal_map_token TEXT NOT NULL,
                    friendly_name TEXT NOT NULL,
                    final_round INTEGER NOT NULL,
                    final_points INTEGER NOT NULL,
                    final_kills INTEGER NOT NULL,
                    final_downs INTEGER NOT NULL,
                    final_revives INTEGER NOT NULL,
                    final_headshots INTEGER NOT NULL,
                    game_duration_ms INTEGER NULL
                );

                CREATE TABLE IF NOT EXISTS game_history_rounds (
                    game_history_entry_id TEXT NOT NULL,
                    sequence INTEGER NOT NULL,
                    round_number INTEGER NOT NULL,
                    started_at_unix_time_ms INTEGER NOT NULL,
                    ended_at_unix_time_ms INTEGER NOT NULL,
                    cumulative_points INTEGER NOT NULL,
                    cumulative_kills INTEGER NOT NULL,
                    cumulative_downs INTEGER NOT NULL,
                    cumulative_revives INTEGER NOT NULL,
                    cumulative_headshots INTEGER NOT NULL,
                    delta_points INTEGER NOT NULL,
                    delta_kills INTEGER NOT NULL,
                    delta_downs INTEGER NOT NULL,
                    delta_revives INTEGER NOT NULL,
                    delta_headshots INTEGER NOT NULL,
                    round_duration_ms INTEGER NULL,
                    PRIMARY KEY (game_history_entry_id, sequence),
                    FOREIGN KEY (game_history_entry_id)
                        REFERENCES game_history_entries(id)
                        ON DELETE CASCADE
                );

                CREATE TABLE IF NOT EXISTS game_history_box_events (
                    game_history_entry_id TEXT NOT NULL,
                    sequence INTEGER NOT NULL,
                    received_at_unix_time_ms INTEGER NOT NULL,
                    round_number INTEGER NOT NULL,
                    event_name TEXT NOT NULL,
                    raw_weapon_token TEXT NULL,
                    weapon_display_name TEXT NULL,
                    owner_id INTEGER NOT NULL,
                    string_value INTEGER NOT NULL,
                    PRIMARY KEY (game_history_entry_id, sequence),
                    FOREIGN KEY (game_history_entry_id)
                        REFERENCES game_history_entries(id)
                        ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS ix_game_history_entries_summary_order
                    ON game_history_entries (
                        ended_at_unix_time_ms DESC,
                        started_at_unix_time_ms DESC,
                        id ASC
                    );

                CREATE INDEX IF NOT EXISTS ix_game_history_rounds_entry_round
                    ON game_history_rounds (game_history_entry_id, round_number, sequence);

                CREATE INDEX IF NOT EXISTS ix_game_history_box_events_entry_received
                    ON game_history_box_events (
                        game_history_entry_id,
                        received_at_unix_time_ms,
                        sequence
                    );
                """,
                cancellationToken);

            await ExecuteNonQueryAsync(
                connection,
                null,
                """
                INSERT INTO game_history_schema (id, version)
                VALUES (1, $version)
                ON CONFLICT(id) DO NOTHING;
                """,
                cancellationToken,
                ("$version", CurrentSchemaVersion));

            int schemaVersion = await ReadSchemaVersionAsync(connection, cancellationToken);
            if (schemaVersion > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Game History schema version {0} is newer than supported version {1}.",
                        schemaVersion,
                        CurrentSchemaVersion));
            }

            if (schemaVersion < CurrentSchemaVersion)
            {
                await ExecuteNonQueryAsync(
                    connection,
                    null,
                    "UPDATE game_history_schema SET version = $version WHERE id = 1;",
                    cancellationToken,
                    ("$version", CurrentSchemaVersion));
            }
        }

        private static async Task<int> ReadSchemaVersionAsync(
            SqliteConnection connection,
            CancellationToken cancellationToken)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT version FROM game_history_schema WHERE id = 1;";
            object? result = await command.ExecuteScalarAsync(cancellationToken);
            return result is null || result is DBNull
                ? 0
                : Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }

        private static async Task InsertEntryAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            GameHistoryEntry entry,
            CancellationToken cancellationToken)
        {
            entry.Normalize();

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                INSERT INTO game_history_entries (
                    id,
                    started_at_unix_time_ms,
                    ended_at_unix_time_ms,
                    base_map_token,
                    start_location_token,
                    internal_map_token,
                    friendly_name,
                    final_round,
                    final_points,
                    final_kills,
                    final_downs,
                    final_revives,
                    final_headshots,
                    game_duration_ms
                )
                VALUES (
                    $id,
                    $started_at_unix_time_ms,
                    $ended_at_unix_time_ms,
                    $base_map_token,
                    $start_location_token,
                    $internal_map_token,
                    $friendly_name,
                    $final_round,
                    $final_points,
                    $final_kills,
                    $final_downs,
                    $final_revives,
                    $final_headshots,
                    $game_duration_ms
                );
                """,
                cancellationToken,
                ("$id", entry.Id),
                ("$started_at_unix_time_ms", ToUnixTimeMilliseconds(entry.StartedAt)),
                ("$ended_at_unix_time_ms", ToUnixTimeMilliseconds(entry.EndedAt)),
                ("$base_map_token", entry.MapIdentity.BaseMapToken),
                ("$start_location_token", entry.MapIdentity.StartLocationToken),
                ("$internal_map_token", entry.MapIdentity.InternalMapToken),
                ("$friendly_name", entry.MapIdentity.FriendlyName),
                ("$final_round", entry.FinalRound),
                ("$final_points", entry.FinalStats.Points),
                ("$final_kills", entry.FinalStats.Kills),
                ("$final_downs", entry.FinalStats.Downs),
                ("$final_revives", entry.FinalStats.Revives),
                ("$final_headshots", entry.FinalStats.Headshots),
                ("$game_duration_ms", ToDurationMilliseconds(entry.GameDuration)));

            for (int i = 0; i < entry.Rounds.Count; i++)
            {
                await InsertRoundAsync(connection, transaction, entry.Id, i, entry.Rounds[i], cancellationToken);
            }

            for (int i = 0; i < entry.BoxEvents.Count; i++)
            {
                await InsertBoxEventAsync(
                    connection,
                    transaction,
                    entry.Id,
                    i,
                    entry.BoxEvents[i],
                    cancellationToken);
            }
        }

        private static Task InsertRoundAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string entryId,
            int sequence,
            GameHistoryRound round,
            CancellationToken cancellationToken)
        {
            return ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                INSERT INTO game_history_rounds (
                    game_history_entry_id,
                    sequence,
                    round_number,
                    started_at_unix_time_ms,
                    ended_at_unix_time_ms,
                    cumulative_points,
                    cumulative_kills,
                    cumulative_downs,
                    cumulative_revives,
                    cumulative_headshots,
                    delta_points,
                    delta_kills,
                    delta_downs,
                    delta_revives,
                    delta_headshots,
                    round_duration_ms
                )
                VALUES (
                    $game_history_entry_id,
                    $sequence,
                    $round_number,
                    $started_at_unix_time_ms,
                    $ended_at_unix_time_ms,
                    $cumulative_points,
                    $cumulative_kills,
                    $cumulative_downs,
                    $cumulative_revives,
                    $cumulative_headshots,
                    $delta_points,
                    $delta_kills,
                    $delta_downs,
                    $delta_revives,
                    $delta_headshots,
                    $round_duration_ms
                );
                """,
                cancellationToken,
                ("$game_history_entry_id", entryId),
                ("$sequence", sequence),
                ("$round_number", round.RoundNumber),
                ("$started_at_unix_time_ms", ToUnixTimeMilliseconds(round.StartedAt)),
                ("$ended_at_unix_time_ms", ToUnixTimeMilliseconds(round.EndedAt)),
                ("$cumulative_points", round.CumulativeStats.Points),
                ("$cumulative_kills", round.CumulativeStats.Kills),
                ("$cumulative_downs", round.CumulativeStats.Downs),
                ("$cumulative_revives", round.CumulativeStats.Revives),
                ("$cumulative_headshots", round.CumulativeStats.Headshots),
                ("$delta_points", round.DeltaStats.Points),
                ("$delta_kills", round.DeltaStats.Kills),
                ("$delta_downs", round.DeltaStats.Downs),
                ("$delta_revives", round.DeltaStats.Revives),
                ("$delta_headshots", round.DeltaStats.Headshots),
                ("$round_duration_ms", ToDurationMilliseconds(round.RoundDuration)));
        }

        private static Task InsertBoxEventAsync(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string entryId,
            int sequence,
            GameHistoryBoxEvent boxEvent,
            CancellationToken cancellationToken)
        {
            return ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                INSERT INTO game_history_box_events (
                    game_history_entry_id,
                    sequence,
                    received_at_unix_time_ms,
                    round_number,
                    event_name,
                    raw_weapon_token,
                    weapon_display_name,
                    owner_id,
                    string_value
                )
                VALUES (
                    $game_history_entry_id,
                    $sequence,
                    $received_at_unix_time_ms,
                    $round_number,
                    $event_name,
                    $raw_weapon_token,
                    $weapon_display_name,
                    $owner_id,
                    $string_value
                );
                """,
                cancellationToken,
                ("$game_history_entry_id", entryId),
                ("$sequence", sequence),
                ("$received_at_unix_time_ms", ToUnixTimeMilliseconds(boxEvent.ReceivedAt)),
                ("$round_number", boxEvent.RoundNumber),
                ("$event_name", boxEvent.EventName),
                ("$raw_weapon_token", boxEvent.RawWeaponToken),
                ("$weapon_display_name", boxEvent.WeaponDisplayName),
                ("$owner_id", (long)boxEvent.OwnerId),
                ("$string_value", (long)boxEvent.StringValue));
        }

        private static async Task<IReadOnlyList<GameHistoryRound>> LoadRoundsAsync(
            SqliteConnection connection,
            string entryId,
            CancellationToken cancellationToken)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    round_number,
                    started_at_unix_time_ms,
                    ended_at_unix_time_ms,
                    cumulative_points,
                    cumulative_kills,
                    cumulative_downs,
                    cumulative_revives,
                    cumulative_headshots,
                    delta_points,
                    delta_kills,
                    delta_downs,
                    delta_revives,
                    delta_headshots,
                    round_duration_ms
                FROM game_history_rounds
                WHERE game_history_entry_id = $game_history_entry_id
                ORDER BY round_number ASC, sequence ASC;
                """;
            command.Parameters.AddWithValue("$game_history_entry_id", entryId);

            List<GameHistoryRound> rounds = [];
            using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rounds.Add(new GameHistoryRound
                {
                    RoundNumber = reader.GetInt32(0),
                    StartedAt = FromUnixTimeMilliseconds(reader.GetInt64(1)),
                    EndedAt = FromUnixTimeMilliseconds(reader.GetInt64(2)),
                    CumulativeStats = new GameHistoryStats
                    {
                        Points = reader.GetInt32(3),
                        Kills = reader.GetInt32(4),
                        Downs = reader.GetInt32(5),
                        Revives = reader.GetInt32(6),
                        Headshots = reader.GetInt32(7)
                    },
                    DeltaStats = new GameHistoryStats
                    {
                        Points = reader.GetInt32(8),
                        Kills = reader.GetInt32(9),
                        Downs = reader.GetInt32(10),
                        Revives = reader.GetInt32(11),
                        Headshots = reader.GetInt32(12)
                    },
                    RoundDuration = ReadNullableDuration(reader, 13)
                });
            }

            return rounds;
        }

        private static async Task<IReadOnlyList<GameHistoryBoxEvent>> LoadBoxEventsAsync(
            SqliteConnection connection,
            string entryId,
            CancellationToken cancellationToken)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    received_at_unix_time_ms,
                    round_number,
                    event_name,
                    raw_weapon_token,
                    weapon_display_name,
                    owner_id,
                    string_value
                FROM game_history_box_events
                WHERE game_history_entry_id = $game_history_entry_id
                ORDER BY received_at_unix_time_ms ASC, sequence ASC;
                """;
            command.Parameters.AddWithValue("$game_history_entry_id", entryId);

            List<GameHistoryBoxEvent> boxEvents = [];
            using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                boxEvents.Add(new GameHistoryBoxEvent
                {
                    ReceivedAt = FromUnixTimeMilliseconds(reader.GetInt64(0)),
                    RoundNumber = reader.GetInt32(1),
                    EventName = reader.GetString(2),
                    RawWeaponToken = reader.IsDBNull(3) ? null : reader.GetString(3),
                    WeaponDisplayName = reader.IsDBNull(4) ? null : reader.GetString(4),
                    OwnerId = (uint)reader.GetInt64(5),
                    StringValue = (uint)reader.GetInt64(6)
                });
            }

            return boxEvents;
        }

        private static async Task ExecuteNonQueryAsync(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            string commandText,
            CancellationToken cancellationToken,
            params (string Name, object? Value)[] parameters)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = commandText;
            foreach ((string name, object? value) in parameters)
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        private static GameHistorySummary ReadSummary(SqliteDataReader reader)
        {
            return new GameHistorySummary(
                reader.GetString(0),
                FromUnixTimeMilliseconds(reader.GetInt64(1)),
                FromUnixTimeMilliseconds(reader.GetInt64(2)),
                new GameHistoryMapIdentity(
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6)),
                reader.GetInt32(7),
                new GameHistoryStats
                {
                    Points = reader.GetInt32(8),
                    Kills = reader.GetInt32(9),
                    Downs = reader.GetInt32(10),
                    Revives = reader.GetInt32(11),
                    Headshots = reader.GetInt32(12)
                },
                ReadNullableDuration(reader, 13));
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

        private static long ToUnixTimeMilliseconds(DateTimeOffset value)
        {
            return value.ToUniversalTime().ToUnixTimeMilliseconds();
        }

        private static DateTimeOffset FromUnixTimeMilliseconds(long value)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(value);
        }

        private static long? ToDurationMilliseconds(TimeSpan? value)
        {
            return value is TimeSpan duration
                ? checked(duration.Ticks / TimeSpan.TicksPerMillisecond)
                : null;
        }

        private static TimeSpan? ReadNullableDuration(SqliteDataReader reader, int ordinal)
        {
            return reader.IsDBNull(ordinal)
                ? null
                : TimeSpan.FromTicks(checked(reader.GetInt64(ordinal) * TimeSpan.TicksPerMillisecond));
        }
    }
}
