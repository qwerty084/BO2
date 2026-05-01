using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace BO2.Services
{
    public sealed record WidgetSettingsLoadRecovery(string Reason, string? BackupPath, string? ErrorMessage);

    public sealed class WidgetSettingsStore
    {
        private readonly string _settingsPath;

        public WidgetSettingsStore(string settingsPath)
        {
            _settingsPath = settingsPath;
        }

        public WidgetSettingsLoadRecovery? LastLoadRecovery { get; private set; }

        public static WidgetSettingsStore CreateDefault()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string settingsPath = Path.GetFullPath(Path.Join(appData, "BO2", "widgets.json"));
            return new WidgetSettingsStore(settingsPath);
        }

        public WidgetSettingsDocument Load()
        {
            LastLoadRecovery = null;

            try
            {
                if (!File.Exists(_settingsPath))
                {
                    return WidgetSettingsDocument.CreateDefault();
                }

                string json = File.ReadAllText(_settingsPath);
                WidgetSettingsDocument? document = JsonSerializer.Deserialize(
                    json,
                    SettingsJsonSerializerContext.Default.WidgetSettingsDocument);
                if (document is null)
                {
                    return RecoverDefault("Widget settings document was empty.");
                }

                if (document.Version > WidgetSettingsDocument.CurrentVersion)
                {
                    return RecoverDefault(
                        $"Widget settings version {document.Version.ToString(CultureInfo.InvariantCulture)} is newer than supported version {WidgetSettingsDocument.CurrentVersion.ToString(CultureInfo.InvariantCulture)}.");
                }

                document.Normalize();
                return document;
            }
            catch (IOException ex)
            {
                return RecoverDefault("Widget settings file could not be read.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                return RecoverDefault("Widget settings file could not be accessed.", ex);
            }
            catch (JsonException ex)
            {
                return RecoverDefault("Widget settings JSON was invalid.", ex);
            }
        }

        public void Save(WidgetSettingsDocument document)
        {
            document.Normalize();
            string? directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = _settingsPath + ".tmp";
            string json = JsonSerializer.Serialize(
                document,
                SettingsJsonSerializerContext.Default.WidgetSettingsDocument);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _settingsPath, overwrite: true);
        }

        private WidgetSettingsDocument RecoverDefault(string reason, Exception? exception = null)
        {
            string? backupPath = TryMoveInvalidSettingsFile();
            LastLoadRecovery = new WidgetSettingsLoadRecovery(reason, backupPath, exception?.Message);
            return WidgetSettingsDocument.CreateDefault();
        }

        private string? TryMoveInvalidSettingsFile()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    return null;
                }

                string backupPath = CreateBackupPath();
                File.Move(_settingsPath, backupPath);
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
            string prefix = _settingsPath + ".invalid-" + timestamp;
            for (int i = 0; i < 1000; i++)
            {
                string suffix = i == 0 ? ".bak" : "-" + i.ToString(CultureInfo.InvariantCulture) + ".bak";
                string candidate = prefix + suffix;
                if (!File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return _settingsPath + ".invalid-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".bak";
        }
    }
}
