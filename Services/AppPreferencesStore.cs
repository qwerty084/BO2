using System;
using System.IO;
using System.Text.Json;

namespace BO2.Services
{
    public sealed class AppPreferencesStore
    {
        private readonly string _preferencesPath;

        public AppPreferencesStore(string preferencesPath)
        {
            _preferencesPath = preferencesPath;
        }

        public static AppPreferencesStore CreateDefault()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string preferencesPath = Path.Combine(appData, "BO2", "preferences.json");
            return new AppPreferencesStore(preferencesPath);
        }

        public AppPreferences Load()
        {
            try
            {
                if (!File.Exists(_preferencesPath))
                {
                    return AppPreferences.CreateDefault();
                }

                string json = File.ReadAllText(_preferencesPath);
                AppPreferences? preferences = JsonSerializer.Deserialize(
                    json,
                    SettingsJsonSerializerContext.Default.AppPreferences);
                if (preferences is null || preferences.Version > AppPreferences.CurrentVersion)
                {
                    return AppPreferences.CreateDefault();
                }

                preferences.Normalize();
                return preferences;
            }
            catch (IOException)
            {
                return AppPreferences.CreateDefault();
            }
            catch (UnauthorizedAccessException)
            {
                return AppPreferences.CreateDefault();
            }
            catch (JsonException)
            {
                return AppPreferences.CreateDefault();
            }
        }

        public void Save(AppPreferences preferences)
        {
            preferences.Normalize();
            string? directory = Path.GetDirectoryName(_preferencesPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = _preferencesPath + ".tmp";
            string json = JsonSerializer.Serialize(
                preferences,
                SettingsJsonSerializerContext.Default.AppPreferences);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _preferencesPath, overwrite: true);
        }
    }
}
