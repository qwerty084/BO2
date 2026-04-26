using System;
using System.IO;
using System.Text.Json;

namespace BO2.Services
{
    public sealed class WidgetSettingsStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        private readonly string _settingsPath;

        public WidgetSettingsStore(string settingsPath)
        {
            _settingsPath = settingsPath;
        }

        public static WidgetSettingsStore CreateDefault()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string settingsPath = Path.Combine(appData, "BO2", "widgets.json");
            return new WidgetSettingsStore(settingsPath);
        }

        public WidgetSettingsDocument Load()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    return WidgetSettingsDocument.CreateDefault();
                }

                string json = File.ReadAllText(_settingsPath);
                WidgetSettingsDocument? document = JsonSerializer.Deserialize<WidgetSettingsDocument>(json, JsonOptions);
                if (document is null || document.Version > WidgetSettingsDocument.CurrentVersion)
                {
                    return WidgetSettingsDocument.CreateDefault();
                }

                document.Normalize();
                return document;
            }
            catch (IOException)
            {
                return WidgetSettingsDocument.CreateDefault();
            }
            catch (UnauthorizedAccessException)
            {
                return WidgetSettingsDocument.CreateDefault();
            }
            catch (JsonException)
            {
                return WidgetSettingsDocument.CreateDefault();
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
            string json = JsonSerializer.Serialize(document, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _settingsPath, overwrite: true);
        }
    }
}
