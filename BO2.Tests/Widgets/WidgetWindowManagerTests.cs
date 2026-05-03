using System;
using System.IO;
using BO2.Services;
using BO2.Widgets;
using Xunit;

namespace BO2.Tests.Widgets
{
    public sealed class WidgetWindowManagerTests
    {
        [Fact]
        public void ManualBoxTrackerClose_RaisesSettingsChangedAndPersistsDisabledSettings()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var store = new WidgetSettingsStore(CreateSettingsPath());
            using var manager = new WidgetWindowManager(
                store,
                new BoxTrackerWidgetRuntime(adapter));
            manager.SetBoxTrackerEnabled(true);
            FakeBoxTrackerWidgetNativeWindow window = Assert.IsType<FakeBoxTrackerWidgetNativeWindow>(
                adapter.CreatedWindow);
            int settingsChangedCallCount = 0;
            manager.SettingsChanged += (_, _) => settingsChangedCallCount++;

            window.SimulateManualClose();

            Assert.Equal(1, settingsChangedCallCount);
            Assert.False(manager.IsBoxTrackerEnabled);

            WidgetSettings persistedSettings = store.Load().GetWidget(WidgetKind.BoxTracker);
            Assert.False(persistedSettings.Enabled);
            Assert.Equal(640, persistedSettings.X);
            Assert.Equal(360, persistedSettings.Y);
        }

        [Fact]
        public void Dispose_ShutsDownRuntimeAndPersistsPlacementWithoutDisablingOrNotifying()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var store = new WidgetSettingsStore(CreateSettingsPath());
            var manager = new WidgetWindowManager(
                store,
                new BoxTrackerWidgetRuntime(adapter));
            manager.SetBoxTrackerEnabled(true);
            FakeBoxTrackerWidgetNativeWindow window = Assert.IsType<FakeBoxTrackerWidgetNativeWindow>(
                adapter.CreatedWindow);
            int settingsChangedCallCount = 0;
            manager.SettingsChanged += (_, _) => settingsChangedCallCount++;

            manager.Dispose();

            Assert.Equal(0, settingsChangedCallCount);
            Assert.True(manager.IsBoxTrackerEnabled);
            Assert.Equal(1, window.CapturePlacementCallCount);
            Assert.Equal(1, window.CloseCallCount);

            WidgetSettings persistedSettings = store.Load().GetWidget(WidgetKind.BoxTracker);
            Assert.True(persistedSettings.Enabled);
            Assert.Equal(640, persistedSettings.X);
            Assert.Equal(360, persistedSettings.Y);
        }

        [Fact]
        public void ApplyBoxTrackerSettings_WhenEnabledAndWindowClosed_OpensPersistsAndAppliesNormalizedSettings()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var store = new WidgetSettingsStore(CreateSettingsPath());
            using var manager = new WidgetWindowManager(
                store,
                new BoxTrackerWidgetRuntime(adapter));
            WidgetSettings settings = CreateInvalidEnabledSettings();
            int settingsChangedCallCount = 0;
            manager.SettingsChanged += (_, _) => settingsChangedCallCount++;

            manager.ApplyBoxTrackerSettings(settings);

            FakeBoxTrackerWidgetNativeWindow window = Assert.IsType<FakeBoxTrackerWidgetNativeWindow>(
                adapter.CreatedWindow);
            Assert.True(manager.IsBoxTrackerEnabled);
            Assert.Equal(1, settingsChangedCallCount);
            Assert.Equal(1, adapter.CreateWindowCallCount);
            Assert.Equal(1, window.UpdateTextCallCount);
            Assert.Equal(1, window.ActivateCallCount);
            Assert.Equal(1, window.ApplySettingsCallCount);

            AssertNormalizedAppliedAppearance(window.AppliedSettingsSnapshot);
            WidgetSettings persistedSettings = store.Load().GetWidget(WidgetKind.BoxTracker);
            AssertNormalizedAppliedAppearance(persistedSettings);
        }

        [Fact]
        public void ApplyBoxTrackerSettings_WhenEnabledAndWindowOpen_UpdatesExistingNativeAdapter()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var store = new WidgetSettingsStore(CreateSettingsPath());
            using var manager = new WidgetWindowManager(
                store,
                new BoxTrackerWidgetRuntime(adapter));
            manager.SetBoxTrackerEnabled(true);
            FakeBoxTrackerWidgetNativeWindow window = Assert.IsType<FakeBoxTrackerWidgetNativeWindow>(
                adapter.CreatedWindow);
            WidgetSettings settings = CreateValidEnabledSettings();
            int settingsChangedCallCount = 0;
            manager.SettingsChanged += (_, _) => settingsChangedCallCount++;

            manager.ApplyBoxTrackerSettings(settings);

            Assert.Equal(1, settingsChangedCallCount);
            Assert.Equal(1, adapter.CreateWindowCallCount);
            Assert.Equal(2, window.ApplySettingsCallCount);
            WidgetSettings appliedSettings = Assert.IsType<WidgetSettings>(window.AppliedSettingsSnapshot);
            Assert.Equal(720, appliedSettings.Width);
            Assert.Equal(280, appliedSettings.Height);
            Assert.Equal(120, appliedSettings.X);
            Assert.Equal(220, appliedSettings.Y);
            Assert.Equal("#FF102030", appliedSettings.BackgroundColor);
            Assert.Equal("#FFFFCC00", appliedSettings.TextColor);
            Assert.True(appliedSettings.TransparentBackground);
            Assert.True(appliedSettings.AlwaysOnTop);
            Assert.False(appliedSettings.CenterAlign);

            WidgetSettings persistedSettings = store.Load().GetWidget(WidgetKind.BoxTracker);
            Assert.Equal(720, persistedSettings.Width);
            Assert.Equal(280, persistedSettings.Height);
            Assert.Equal(120, persistedSettings.X);
            Assert.Equal(220, persistedSettings.Y);
            Assert.Equal("#FF102030", persistedSettings.BackgroundColor);
            Assert.Equal("#FFFFCC00", persistedSettings.TextColor);
            Assert.True(persistedSettings.TransparentBackground);
            Assert.True(persistedSettings.AlwaysOnTop);
            Assert.False(persistedSettings.CenterAlign);
        }

        [Fact]
        public void ApplyBoxTrackerSettings_WhenDisabledAndWindowOpen_ClosesPersistsAndNotifies()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var store = new WidgetSettingsStore(CreateSettingsPath());
            using var manager = new WidgetWindowManager(
                store,
                new BoxTrackerWidgetRuntime(adapter));
            manager.SetBoxTrackerEnabled(true);
            FakeBoxTrackerWidgetNativeWindow window = Assert.IsType<FakeBoxTrackerWidgetNativeWindow>(
                adapter.CreatedWindow);
            WidgetSettings settings = WidgetSettings.CreateDefault();
            int settingsChangedCallCount = 0;
            manager.SettingsChanged += (_, _) => settingsChangedCallCount++;

            manager.ApplyBoxTrackerSettings(settings);

            Assert.Equal(1, settingsChangedCallCount);
            Assert.False(manager.IsBoxTrackerEnabled);
            Assert.Equal(1, window.CapturePlacementCallCount);
            Assert.Equal(1, window.CloseCallCount);

            WidgetSettings persistedSettings = store.Load().GetWidget(WidgetKind.BoxTracker);
            Assert.False(persistedSettings.Enabled);
            Assert.Equal(640, persistedSettings.X);
            Assert.Equal(360, persistedSettings.Y);
        }

        private static WidgetSettings CreateInvalidEnabledSettings()
        {
            return new WidgetSettings
            {
                Enabled = true,
                Width = 1,
                Height = 9999,
                X = 120,
                Y = 220,
                BackgroundColor = "bad",
                TextColor = "bad",
                TransparentBackground = true,
                AlwaysOnTop = true,
                CenterAlign = false
            };
        }

        private static WidgetSettings CreateValidEnabledSettings()
        {
            return new WidgetSettings
            {
                Enabled = true,
                Width = 720,
                Height = 280,
                X = 120,
                Y = 220,
                BackgroundColor = "#FF102030",
                TextColor = "#FFFFCC00",
                TransparentBackground = true,
                AlwaysOnTop = true,
                CenterAlign = false
            };
        }

        private static void AssertNormalizedAppliedAppearance(WidgetSettings? settings)
        {
            settings = Assert.IsType<WidgetSettings>(settings);
            Assert.True(settings.Enabled);
            Assert.Equal(160, settings.Width);
            Assert.Equal(2160, settings.Height);
            Assert.Equal(120, settings.X);
            Assert.Equal(220, settings.Y);
            Assert.Equal(WidgetSettings.DefaultBackgroundColor, settings.BackgroundColor);
            Assert.Equal(WidgetSettings.DefaultTextColor, settings.TextColor);
            Assert.True(settings.TransparentBackground);
            Assert.True(settings.AlwaysOnTop);
            Assert.False(settings.CenterAlign);
        }

        private static string CreateSettingsPath()
        {
            return Path.GetFullPath(Path.Join(
                Path.GetTempPath(),
                "BO2.Tests",
                Guid.NewGuid().ToString("N"),
                "widgets.json"));
        }

        private sealed class FakeBoxTrackerWidgetNativeAdapter : IBoxTrackerWidgetNativeAdapter
        {
            public int CreateWindowCallCount { get; private set; }

            public IBoxTrackerWidgetNativeWindow? CreatedWindow { get; private set; }

            public IBoxTrackerWidgetNativeWindow CreateWindow()
            {
                CreateWindowCallCount++;
                CreatedWindow = new FakeBoxTrackerWidgetNativeWindow();
                return CreatedWindow;
            }
        }

        private sealed class FakeBoxTrackerWidgetNativeWindow : IBoxTrackerWidgetNativeWindow
        {
            private EventHandler? _closed;

            public event EventHandler? Closed
            {
                add => _closed += value;
                remove => _closed -= value;
            }

            public int CapturePlacementCallCount { get; private set; }

            public int ActivateCallCount { get; private set; }

            public int ApplySettingsCallCount { get; private set; }

            public int CloseCallCount { get; private set; }

            public int UpdateTextCallCount { get; private set; }

            public WidgetSettings? AppliedSettingsSnapshot { get; private set; }

            public void Activate()
            {
                ActivateCallCount++;
            }

            public void Close()
            {
                CloseCallCount++;
                _closed?.Invoke(this, EventArgs.Empty);
            }

            public void UpdateText(string text)
            {
                UpdateTextCallCount++;
            }

            public void ApplySettings(WidgetSettings settings)
            {
                ApplySettingsCallCount++;
                AppliedSettingsSnapshot = settings.Clone();
            }

            public void CapturePlacement(WidgetSettings settings)
            {
                CapturePlacementCallCount++;
                settings.X = 640;
                settings.Y = 360;
            }

            public void SimulateManualClose()
            {
                _closed?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
