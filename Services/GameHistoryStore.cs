using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BO2.Services
{
    internal sealed record GameHistoryLoadRecovery(string Reason, string? BackupPath, string? ErrorMessage);

    internal sealed class GameHistoryStore(string historyPath)
    {
        private readonly string _historyPath = historyPath;

        public GameHistoryLoadRecovery? LastLoadRecovery { get; private set; }

        public static GameHistoryStore CreateDefault()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string historyPath = Path.GetFullPath(Path.Join(appData, "BO2", "game-history.json"));
            return new GameHistoryStore(historyPath);
        }

        public GameHistoryDocument Load()
        {
            LastLoadRecovery = null;

            try
            {
                if (!File.Exists(_historyPath))
                {
                    return GameHistoryDocument.CreateDefault();
                }

                string json = File.ReadAllText(_historyPath);
                GameHistoryDocument? document = JsonSerializer.Deserialize(
                    json,
                    SettingsJsonSerializerContext.Default.GameHistoryDocument);
                if (document is null)
                {
                    return RecoverDefault("Game history document was empty.");
                }

                if (document.Version > GameHistoryDocument.CurrentVersion)
                {
                    return RecoverDefault(
                        $"Game history version {document.Version.ToString(CultureInfo.InvariantCulture)} is newer than supported version {GameHistoryDocument.CurrentVersion.ToString(CultureInfo.InvariantCulture)}.");
                }

                document.Normalize();
                return document;
            }
            catch (IOException ex)
            {
                return RecoverDefault("Game history file could not be read.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return RecoverDefault("Game history file could not be accessed.", ex);
            }
            catch (JsonException ex)
            {
                return RecoverDefault("Game history JSON was invalid.", ex);
            }
        }

        public void Save(GameHistoryDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            document.Normalize();
            string? directory = Path.GetDirectoryName(_historyPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = _historyPath + ".tmp";
            string json = JsonSerializer.Serialize(
                document,
                SettingsJsonSerializerContext.Default.GameHistoryDocument);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _historyPath, overwrite: true);
        }

        public void Append(GameHistoryEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            GameHistoryDocument document = Load();
            document.Entries.Add(entry);
            Save(document);
        }

        public IReadOnlyList<GameHistorySummary> LoadSummariesNewestFirst()
        {
            return GameHistorySummaryProjector.ProjectNewestFirst(Load());
        }

        private GameHistoryDocument RecoverDefault(string reason, Exception? exception = null)
        {
            string? backupPath = TryMoveInvalidHistoryFile();
            LastLoadRecovery = new GameHistoryLoadRecovery(reason, backupPath, exception?.Message);
            return GameHistoryDocument.CreateDefault();
        }

        private string? TryMoveInvalidHistoryFile()
        {
            try
            {
                if (!File.Exists(_historyPath))
                {
                    return null;
                }

                string backupPath = CreateBackupPath();
                File.Move(_historyPath, backupPath);
                return backupPath;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        private string CreateBackupPath()
        {
            string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            string prefix = _historyPath + ".invalid-" + timestamp;
            for (int i = 0; i < 1000; i++)
            {
                string suffix = i == 0 ? ".bak" : "-" + i.ToString(CultureInfo.InvariantCulture) + ".bak";
                string candidate = prefix + suffix;
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return _historyPath + ".invalid-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".bak";
        }
    }

    internal static class GameHistorySummaryProjector
    {
        public static IReadOnlyList<GameHistorySummary> ProjectNewestFirst(GameHistoryDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);

            document.Normalize();
            return [.. document.Entries
                .OrderByDescending(static entry => entry.EndedAt)
                .ThenByDescending(static entry => entry.StartedAt)
                .Select(static entry => new GameHistorySummary(
                    entry.Id,
                    entry.StartedAt,
                    entry.EndedAt,
                    entry.MapIdentity,
                    entry.FinalRound,
                    entry.FinalStats,
                    entry.GameDuration))];
        }
    }
}
