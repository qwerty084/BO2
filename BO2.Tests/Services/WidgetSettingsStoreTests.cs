using System;
using System.IO;
using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class WidgetSettingsStoreTests
    {
        [Fact]
        public void Load_WhenFileIsMissing_ReturnsDefaultBoxTrackerSettings()
        {
            string settingsPath = CreateSettingsPath();
            var store = new WidgetSettingsStore(settingsPath);

            WidgetSettingsDocument document = store.Load();
            WidgetSettings settings = document.GetWidget(WidgetKind.BoxTracker);

            Assert.Equal(WidgetSettingsDocument.CurrentVersion, document.Version);
            Assert.False(settings.Enabled);
            Assert.Equal(320, settings.Width);
            Assert.Equal(160, settings.Height);
            Assert.Equal("#FFFFFFFF", settings.BackgroundColor);
            Assert.Equal("#FF000000", settings.TextColor);
            Assert.False(settings.TransparentBackground);
            Assert.False(settings.AlwaysOnTop);
            Assert.True(settings.CenterAlign);
        }

        [Fact]
        public void SaveAndLoad_PreservesBoxTrackerSettings()
        {
            string settingsPath = CreateSettingsPath();
            var store = new WidgetSettingsStore(settingsPath);
            WidgetSettingsDocument document = WidgetSettingsDocument.CreateDefault();
            document.SetWidget(
                WidgetKind.BoxTracker,
                new WidgetSettings
                {
                    Enabled = true,
                    Width = 640,
                    Height = 240,
                    X = 100,
                    Y = 200,
                    BackgroundColor = "#AA102030",
                    TextColor = "#FFFFCC00",
                    TransparentBackground = true,
                    AlwaysOnTop = true,
                    CenterAlign = false
                });

            store.Save(document);
            WidgetSettings loaded = store.Load().GetWidget(WidgetKind.BoxTracker);

            Assert.True(loaded.Enabled);
            Assert.Equal(640, loaded.Width);
            Assert.Equal(240, loaded.Height);
            Assert.Equal(100, loaded.X);
            Assert.Equal(200, loaded.Y);
            Assert.Equal("#AA102030", loaded.BackgroundColor);
            Assert.Equal("#FFFFCC00", loaded.TextColor);
            Assert.True(loaded.TransparentBackground);
            Assert.True(loaded.AlwaysOnTop);
            Assert.False(loaded.CenterAlign);
        }

        [Fact]
        public void Load_WhenJsonIsInvalid_ReturnsDefaultSettings()
        {
            string settingsPath = CreateSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, "{not json");
            var store = new WidgetSettingsStore(settingsPath);

            WidgetSettings settings = store.Load().GetWidget(WidgetKind.BoxTracker);

            Assert.False(settings.Enabled);
            Assert.Equal(WidgetSettings.DefaultWidth, settings.Width);
            Assert.Equal(WidgetSettings.DefaultHeight, settings.Height);
        }

        [Fact]
        public void Load_WhenJsonIsInvalid_MovesBadFileToBackup()
        {
            string settingsPath = CreateSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, "{not json");
            var store = new WidgetSettingsStore(settingsPath);

            _ = store.Load();

            Assert.False(File.Exists(settingsPath));
            WidgetSettingsLoadRecovery recovery = Assert.IsType<WidgetSettingsLoadRecovery>(store.LastLoadRecovery);
            Assert.NotNull(recovery.BackupPath);
            Assert.True(File.Exists(recovery.BackupPath));
            Assert.Equal("{not json", File.ReadAllText(recovery.BackupPath));
        }

        [Fact]
        public void Load_WhenVersionIsNewer_MovesUnsupportedFileToBackup()
        {
            string settingsPath = CreateSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(
                settingsPath,
                """
                {
                  "Version": 999,
                  "Widgets": {
                    "BoxTracker": {
                      "Enabled": true
                    }
                  }
                }
                """);
            var store = new WidgetSettingsStore(settingsPath);

            WidgetSettings settings = store.Load().GetWidget(WidgetKind.BoxTracker);

            Assert.False(settings.Enabled);
            Assert.False(File.Exists(settingsPath));
            WidgetSettingsLoadRecovery recovery = Assert.IsType<WidgetSettingsLoadRecovery>(store.LastLoadRecovery);
            Assert.NotNull(recovery.BackupPath);
            Assert.True(File.Exists(recovery.BackupPath));
        }

        [Fact]
        public void Load_WhenValuesAreOutOfRange_NormalizesSettings()
        {
            string settingsPath = CreateSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(
                settingsPath,
                """
                {
                  "Version": 1,
                  "Widgets": {
                    "BoxTracker": {
                      "Width": 1,
                      "Height": 1,
                      "BackgroundColor": "bad",
                      "TextColor": "bad"
                    }
                  }
                }
                """);
            var store = new WidgetSettingsStore(settingsPath);

            WidgetSettings settings = store.Load().GetWidget(WidgetKind.BoxTracker);

            Assert.Equal(160, settings.Width);
            Assert.Equal(80, settings.Height);
            Assert.Equal(WidgetSettings.DefaultBackgroundColor, settings.BackgroundColor);
            Assert.Equal(WidgetSettings.DefaultTextColor, settings.TextColor);
        }

        private static string CreateSettingsPath()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "BO2.Tests",
                Guid.NewGuid().ToString("N"),
                "widgets.json");
        }
    }
}
