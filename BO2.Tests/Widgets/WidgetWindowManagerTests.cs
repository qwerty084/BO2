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
            public IBoxTrackerWidgetNativeWindow? CreatedWindow { get; private set; }

            public IBoxTrackerWidgetNativeWindow CreateWindow()
            {
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

            public int CloseCallCount { get; private set; }

            public void Activate()
            {
            }

            public void Close()
            {
                CloseCallCount++;
                _closed?.Invoke(this, EventArgs.Empty);
            }

            public void UpdateText(string text)
            {
            }

            public void ApplySettings(WidgetSettings settings)
            {
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
