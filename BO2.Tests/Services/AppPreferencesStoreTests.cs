using System;
using System.IO;
using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class AppPreferencesStoreTests
    {
        [Fact]
        public void Load_WhenFileIsMissing_ReturnsDefaultPreferences()
        {
            string preferencesPath = CreatePreferencesPath();
            var store = new AppPreferencesStore(preferencesPath);

            AppPreferences preferences = store.Load();

            Assert.Equal(AppPreferences.CurrentVersion, preferences.Version);
            Assert.Equal(ThemeMode.System, preferences.ThemeMode);
        }

        [Fact]
        public void SaveAndLoad_PreservesThemeMode()
        {
            string preferencesPath = CreatePreferencesPath();
            var store = new AppPreferencesStore(preferencesPath);
            var preferences = new AppPreferences
            {
                ThemeMode = ThemeMode.Dark
            };

            store.Save(preferences);
            AppPreferences loaded = store.Load();

            Assert.Equal(ThemeMode.Dark, loaded.ThemeMode);
        }

        [Fact]
        public void Load_WhenJsonIsInvalid_ReturnsDefaultPreferences()
        {
            string preferencesPath = CreatePreferencesPath();
            Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
            File.WriteAllText(preferencesPath, "{not json");
            var store = new AppPreferencesStore(preferencesPath);

            AppPreferences preferences = store.Load();

            Assert.Equal(ThemeMode.System, preferences.ThemeMode);
        }

        [Fact]
        public void Load_WhenThemeModeIsInvalid_NormalizesToSystem()
        {
            string preferencesPath = CreatePreferencesPath();
            Directory.CreateDirectory(Path.GetDirectoryName(preferencesPath)!);
            File.WriteAllText(
                preferencesPath,
                """
                {
                  "Version": 1,
                  "ThemeMode": 99
                }
                """);
            var store = new AppPreferencesStore(preferencesPath);

            AppPreferences preferences = store.Load();

            Assert.Equal(ThemeMode.System, preferences.ThemeMode);
        }

        private static string CreatePreferencesPath()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "BO2.Tests",
                Guid.NewGuid().ToString("N"),
                "preferences.json");
        }
    }
}
