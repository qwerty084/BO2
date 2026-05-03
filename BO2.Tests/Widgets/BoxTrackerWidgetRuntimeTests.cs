using System;
using System.IO;
using BO2.Services;
using BO2.Widgets;
using Xunit;

namespace BO2.Tests.Widgets
{
    public sealed class BoxTrackerWidgetRuntimeTests
    {
        [Fact]
        public void Constructor_AcceptsFakeNativeAdapter()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();

            var runtime = new BoxTrackerWidgetRuntime(adapter);

            Assert.False(runtime.HasNativeWindow);
            Assert.Equal(0, adapter.CreateWindowCallCount);
        }

        [Fact]
        public void Restore_WhenBoxTrackerDisabled_DoesNotCreateNativeWindow()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var runtime = new BoxTrackerWidgetRuntime(adapter);
            WidgetSettings settings = WidgetSettings.CreateDefault();

            runtime.Restore(settings);

            Assert.False(runtime.HasNativeWindow);
            Assert.Equal(0, adapter.CreateWindowCallCount);
        }

        [Fact]
        public void Restore_WhenBoxTrackerEnabled_CreatesActivatesAndAppliesNativeWindow()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var runtime = new BoxTrackerWidgetRuntime(adapter);
            WidgetSettings settings = WidgetSettings.CreateDefault();
            settings.Enabled = true;

            runtime.Restore(settings);

            FakeBoxTrackerWidgetNativeWindow window = Assert.IsType<FakeBoxTrackerWidgetNativeWindow>(
                adapter.CreatedWindow);
            Assert.True(runtime.HasNativeWindow);
            Assert.Equal(1, adapter.CreateWindowCallCount);
            Assert.Equal(1, window.ActivateCallCount);
            Assert.Same(settings, window.AppliedSettings);
        }

        [Fact]
        public void Restore_WhenBoxTrackerEnabled_InitializesTextFromLatestEventStatus()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var runtime = new BoxTrackerWidgetRuntime(adapter);
            WidgetSettings settings = WidgetSettings.CreateDefault();
            settings.Enabled = true;
            GameEventMonitorStatus status = CreateStatus(
                new GameEvent(
                    GameEventType.BoxEvent,
                    "randomization_done",
                    0,
                    7,
                    1149,
                    new DateTimeOffset(2026, 4, 26, 12, 34, 56, TimeSpan.Zero),
                    "fnfal_zm"));

            runtime.UpdateEventStatus(status);
            runtime.Restore(settings);

            FakeBoxTrackerWidgetNativeWindow window = Assert.IsType<FakeBoxTrackerWidgetNativeWindow>(
                adapter.CreatedWindow);
            Assert.Equal(1, window.UpdateTextCallCount);
            Assert.Contains("BoxEventWithWeaponFormat", window.Text, StringComparison.Ordinal);
            Assert.Contains("randomization_done", window.Text, StringComparison.Ordinal);
            Assert.Contains("FAL (fnfal_zm)", window.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void Restore_WhenBoxTrackerEnabledAndNoBoxEvents_DisplaysEmptyPrompt()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var runtime = new BoxTrackerWidgetRuntime(adapter);
            WidgetSettings settings = WidgetSettings.CreateDefault();
            settings.Enabled = true;
            GameEventMonitorStatus status = CreateStatus(
                new GameEvent(
                    GameEventType.StartOfRound,
                    "start_of_round",
                    1,
                    3,
                    417,
                    new DateTimeOffset(2026, 4, 26, 12, 34, 56, TimeSpan.Zero)));

            runtime.Restore(settings, status);

            FakeBoxTrackerWidgetNativeWindow window = Assert.IsType<FakeBoxTrackerWidgetNativeWindow>(
                adapter.CreatedWindow);
            Assert.Equal("BoxTrackerEmpty", window.Text);
        }

        [Fact]
        public void UpdateEventStatus_WhenBoxTrackerWindowIsOpen_UpdatesNativeWindowText()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var runtime = new BoxTrackerWidgetRuntime(adapter);
            WidgetSettings settings = WidgetSettings.CreateDefault();
            settings.Enabled = true;
            runtime.Restore(settings);
            GameEventMonitorStatus status = CreateStatus(
                new GameEvent(
                    GameEventType.BoxEvent,
                    "randomization_done",
                    0,
                    9,
                    1150,
                    new DateTimeOffset(2026, 4, 26, 12, 35, 56, TimeSpan.Zero),
                    "galil_zm"));

            runtime.UpdateEventStatus(status);

            FakeBoxTrackerWidgetNativeWindow window = Assert.IsType<FakeBoxTrackerWidgetNativeWindow>(
                adapter.CreatedWindow);
            Assert.Equal(2, window.UpdateTextCallCount);
            Assert.Contains("BoxEventWithWeaponFormat", window.Text, StringComparison.Ordinal);
            Assert.Contains("randomization_done", window.Text, StringComparison.Ordinal);
            Assert.Contains("Galil (galil_zm)", window.Text, StringComparison.Ordinal);
            Assert.Contains("9", window.Text, StringComparison.Ordinal);
            Assert.Contains("1150", window.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void SetEnabled_WhenDisabled_OpensActivatesAppliesAndPersistsEnabledSettings()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var runtime = new BoxTrackerWidgetRuntime(adapter);
            WidgetSettingsDocument document = WidgetSettingsDocument.CreateDefault();
            WidgetSettings settings = document.GetWidget(WidgetKind.BoxTracker);
            var store = new WidgetSettingsStore(CreateSettingsPath());
            int persistCallCount = 0;

            bool changed = runtime.SetEnabled(settings, enabled: true, () =>
            {
                persistCallCount++;
                store.Save(document);
            });

            FakeBoxTrackerWidgetNativeWindow window = Assert.IsType<FakeBoxTrackerWidgetNativeWindow>(
                adapter.CreatedWindow);
            Assert.True(changed);
            Assert.True(settings.Enabled);
            Assert.True(runtime.HasNativeWindow);
            Assert.Equal(1, adapter.CreateWindowCallCount);
            Assert.Equal(1, window.UpdateTextCallCount);
            Assert.Equal(1, window.ActivateCallCount);
            Assert.Same(settings, window.AppliedSettings);
            Assert.Equal(1, persistCallCount);
            Assert.True(store.Load().GetWidget(WidgetKind.BoxTracker).Enabled);
        }

        [Fact]
        public void SetEnabled_WhenEnabled_ClosesOpenWindowAndPersistsDisabledSettings()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var runtime = new BoxTrackerWidgetRuntime(adapter);
            WidgetSettingsDocument document = WidgetSettingsDocument.CreateDefault();
            WidgetSettings settings = document.GetWidget(WidgetKind.BoxTracker);
            settings.Enabled = true;
            var store = new WidgetSettingsStore(CreateSettingsPath());
            runtime.Restore(settings);
            FakeBoxTrackerWidgetNativeWindow window = Assert.IsType<FakeBoxTrackerWidgetNativeWindow>(
                adapter.CreatedWindow);
            int persistCallCount = 0;

            bool changed = runtime.SetEnabled(settings, enabled: false, () =>
            {
                persistCallCount++;
                store.Save(document);
            });

            Assert.True(changed);
            Assert.False(settings.Enabled);
            Assert.False(runtime.HasNativeWindow);
            Assert.Equal(1, window.CapturePlacementCallCount);
            Assert.Equal(1, window.CloseCallCount);
            Assert.Equal(640, settings.X);
            Assert.Equal(360, settings.Y);
            Assert.Equal(1, persistCallCount);
            WidgetSettings persistedSettings = store.Load().GetWidget(WidgetKind.BoxTracker);
            Assert.False(persistedSettings.Enabled);
            Assert.Equal(640, persistedSettings.X);
            Assert.Equal(360, persistedSettings.Y);
        }

        [Fact]
        public void SetEnabled_WhenAlreadyEnabled_DoesNotCreateDuplicateWindowOrPersistAgain()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var runtime = new BoxTrackerWidgetRuntime(adapter);
            WidgetSettings settings = WidgetSettings.CreateDefault();
            int persistCallCount = 0;
            runtime.SetEnabled(settings, enabled: true, () => persistCallCount++);

            bool changed = runtime.SetEnabled(settings, enabled: true, () => persistCallCount++);

            FakeBoxTrackerWidgetNativeWindow window = Assert.IsType<FakeBoxTrackerWidgetNativeWindow>(
                adapter.CreatedWindow);
            Assert.False(changed);
            Assert.True(settings.Enabled);
            Assert.True(runtime.HasNativeWindow);
            Assert.Equal(1, adapter.CreateWindowCallCount);
            Assert.Equal(1, window.ActivateCallCount);
            Assert.Equal(1, persistCallCount);
        }

        [Fact]
        public void SetEnabled_WhenAlreadyDisabled_DoesNotCloseOrPersistAgain()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var runtime = new BoxTrackerWidgetRuntime(adapter);
            WidgetSettings settings = WidgetSettings.CreateDefault();
            int persistCallCount = 0;

            bool changed = runtime.SetEnabled(settings, enabled: false, () => persistCallCount++);

            Assert.False(changed);
            Assert.False(settings.Enabled);
            Assert.False(runtime.HasNativeWindow);
            Assert.Equal(0, adapter.CreateWindowCallCount);
            Assert.Equal(0, persistCallCount);
        }

        [Fact]
        public void EnsureNativeWindow_CreatesWindowThroughAdapter()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var runtime = new BoxTrackerWidgetRuntime(adapter);

            IBoxTrackerWidgetNativeWindow window = runtime.EnsureNativeWindow();

            Assert.True(runtime.HasNativeWindow);
            Assert.Same(adapter.CreatedWindow, window);
            Assert.Equal(1, adapter.CreateWindowCallCount);
        }

        private static GameEventMonitorStatus CreateStatus(params GameEvent[] events)
        {
            return new GameEventMonitorStatus(
                GameCompatibilityState.Compatible,
                0,
                0,
                (uint)events.Length,
                events);
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
            public event EventHandler? Closed;

            public int ActivateCallCount { get; private set; }

            public int CapturePlacementCallCount { get; private set; }

            public int CloseCallCount { get; private set; }

            public int UpdateTextCallCount { get; private set; }

            public string Text { get; private set; } = string.Empty;

            public WidgetSettings? AppliedSettings { get; private set; }

            public void Activate()
            {
                ActivateCallCount++;
            }

            public void Close()
            {
                CloseCallCount++;
                Closed?.Invoke(this, EventArgs.Empty);
            }

            public void UpdateText(string text)
            {
                UpdateTextCallCount++;
                Text = text;
            }

            public void ApplySettings(WidgetSettings settings)
            {
                AppliedSettings = settings;
            }

            public void CapturePlacement(WidgetSettings settings)
            {
                CapturePlacementCallCount++;
                settings.X = 640;
                settings.Y = 360;
            }
        }
    }
}
