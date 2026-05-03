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
        public void ApplySettings_WhenEnabledAndWindowClosed_OpensActivatesUpdatesTextAndAppliesSettings()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var runtime = new BoxTrackerWidgetRuntime(adapter);
            WidgetSettings settings = CreateCustomEnabledSettings();
            GameEventMonitorStatus status = CreateStatus(
                new GameEvent(
                    GameEventType.BoxEvent,
                    "randomization_done",
                    0,
                    4,
                    1138,
                    new DateTimeOffset(2026, 4, 26, 12, 36, 56, TimeSpan.Zero),
                    "python_zm"));

            runtime.UpdateEventStatus(status);
            runtime.ApplySettings(settings);

            FakeBoxTrackerWidgetNativeWindow window = Assert.IsType<FakeBoxTrackerWidgetNativeWindow>(
                adapter.CreatedWindow);
            Assert.True(runtime.HasNativeWindow);
            Assert.Equal(1, adapter.CreateWindowCallCount);
            Assert.Equal(1, window.UpdateTextCallCount);
            Assert.Equal(1, window.ActivateCallCount);
            Assert.Equal(1, window.ApplySettingsCallCount);
            Assert.Same(settings, window.AppliedSettings);
            Assert.Contains("Python (python_zm)", window.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void ApplySettings_WhenEnabledAndWindowOpen_UpdatesExistingNativeWindowAppearance()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var runtime = new BoxTrackerWidgetRuntime(adapter);
            WidgetSettings initialSettings = WidgetSettings.CreateDefault();
            initialSettings.Enabled = true;
            runtime.Restore(initialSettings);
            FakeBoxTrackerWidgetNativeWindow window = Assert.IsType<FakeBoxTrackerWidgetNativeWindow>(
                adapter.CreatedWindow);
            WidgetSettings updatedSettings = CreateCustomEnabledSettings();
            updatedSettings.Width = 720;
            updatedSettings.Height = 280;
            updatedSettings.BackgroundColor = "#FF102030";
            updatedSettings.TextColor = "#FFFFCC00";
            updatedSettings.TransparentBackground = true;
            updatedSettings.AlwaysOnTop = true;
            updatedSettings.CenterAlign = false;

            runtime.ApplySettings(updatedSettings);

            Assert.True(runtime.HasNativeWindow);
            Assert.Equal(1, adapter.CreateWindowCallCount);
            Assert.Equal(2, window.ApplySettingsCallCount);
            Assert.Same(updatedSettings, window.AppliedSettings);
            WidgetSettings appliedSettings = Assert.IsType<WidgetSettings>(window.AppliedSettingsSnapshot);
            Assert.Equal(720, appliedSettings.Width);
            Assert.Equal(280, appliedSettings.Height);
            Assert.Equal("#FF102030", appliedSettings.BackgroundColor);
            Assert.Equal("#FFFFCC00", appliedSettings.TextColor);
            Assert.True(appliedSettings.TransparentBackground);
            Assert.True(appliedSettings.AlwaysOnTop);
            Assert.False(appliedSettings.CenterAlign);
        }

        [Fact]
        public void ApplySettings_WhenDisabledAndWindowOpen_ClosesNativeWindowAndCapturesPlacement()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var runtime = new BoxTrackerWidgetRuntime(adapter);
            WidgetSettings initialSettings = WidgetSettings.CreateDefault();
            initialSettings.Enabled = true;
            runtime.Restore(initialSettings);
            FakeBoxTrackerWidgetNativeWindow window = Assert.IsType<FakeBoxTrackerWidgetNativeWindow>(
                adapter.CreatedWindow);
            WidgetSettings disabledSettings = WidgetSettings.CreateDefault();

            runtime.ApplySettings(disabledSettings);

            Assert.False(runtime.HasNativeWindow);
            Assert.Equal(1, window.CapturePlacementCallCount);
            Assert.Equal(1, window.CloseCallCount);
            Assert.Equal(640, disabledSettings.X);
            Assert.Equal(360, disabledSettings.Y);
        }

        [Fact]
        public void ApplySettings_NormalizesBeforeNativeAdapterApplication()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var runtime = new BoxTrackerWidgetRuntime(adapter);
            WidgetSettings settings = WidgetSettings.CreateDefault();
            settings.Enabled = true;
            settings.Width = 1;
            settings.Height = 9999;
            settings.BackgroundColor = "bad";
            settings.TextColor = "bad";

            runtime.ApplySettings(settings);

            FakeBoxTrackerWidgetNativeWindow window = Assert.IsType<FakeBoxTrackerWidgetNativeWindow>(
                adapter.CreatedWindow);
            WidgetSettings appliedSettings = Assert.IsType<WidgetSettings>(window.AppliedSettingsSnapshot);
            Assert.Equal(160, appliedSettings.Width);
            Assert.Equal(2160, appliedSettings.Height);
            Assert.Equal(WidgetSettings.DefaultBackgroundColor, appliedSettings.BackgroundColor);
            Assert.Equal(WidgetSettings.DefaultTextColor, appliedSettings.TextColor);
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
        public void ManualClose_WhenWindowOpen_CapturesPlacementDisablesPersistsAndNotifies()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var runtime = new BoxTrackerWidgetRuntime(adapter);
            WidgetSettingsDocument document = WidgetSettingsDocument.CreateDefault();
            WidgetSettings settings = document.GetWidget(WidgetKind.BoxTracker);
            settings.Enabled = true;
            var store = new WidgetSettingsStore(CreateSettingsPath());
            int persistCallCount = 0;
            int notifyCallCount = 0;

            runtime.Restore(
                settings,
                CreateStatus(),
                () =>
                {
                    persistCallCount++;
                    store.Save(document);
                },
                () => notifyCallCount++);

            FakeBoxTrackerWidgetNativeWindow window = Assert.IsType<FakeBoxTrackerWidgetNativeWindow>(
                adapter.CreatedWindow);
            bool runtimeHadWindowDuringPlacementCapture = false;
            window.CapturePlacementCallback = () => runtimeHadWindowDuringPlacementCapture = runtime.HasNativeWindow;

            window.SimulateManualClose();

            Assert.True(runtimeHadWindowDuringPlacementCapture);
            Assert.False(runtime.HasNativeWindow);
            Assert.False(settings.Enabled);
            Assert.Equal(1, window.CapturePlacementCallCount);
            Assert.Equal(1, persistCallCount);
            Assert.Equal(1, notifyCallCount);

            WidgetSettings persistedSettings = store.Load().GetWidget(WidgetKind.BoxTracker);
            Assert.False(persistedSettings.Enabled);
            Assert.Equal(640, persistedSettings.X);
            Assert.Equal(360, persistedSettings.Y);
        }

        [Fact]
        public void Shutdown_WhenWindowOpen_CapturesPlacementPersistsClosesAndKeepsEnabled()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var runtime = new BoxTrackerWidgetRuntime(adapter);
            WidgetSettingsDocument document = WidgetSettingsDocument.CreateDefault();
            WidgetSettings settings = document.GetWidget(WidgetKind.BoxTracker);
            settings.Enabled = true;
            var store = new WidgetSettingsStore(CreateSettingsPath());
            int persistCallCount = 0;
            int notifyCallCount = 0;
            runtime.Restore(
                settings,
                CreateStatus(),
                () => store.Save(document),
                () => notifyCallCount++);
            FakeBoxTrackerWidgetNativeWindow window = Assert.IsType<FakeBoxTrackerWidgetNativeWindow>(
                adapter.CreatedWindow);

            bool closed = runtime.Shutdown(
                settings,
                () =>
                {
                    persistCallCount++;
                    store.Save(document);
                });

            Assert.True(closed);
            Assert.False(runtime.HasNativeWindow);
            Assert.True(settings.Enabled);
            Assert.Equal(1, window.CapturePlacementCallCount);
            Assert.Equal(1, window.CloseCallCount);
            Assert.Equal(1, persistCallCount);
            Assert.Equal(0, notifyCallCount);

            WidgetSettings persistedSettings = store.Load().GetWidget(WidgetKind.BoxTracker);
            Assert.True(persistedSettings.Enabled);
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
            FakeBoxTrackerWidgetNativeWindow fakeWindow = Assert.IsType<FakeBoxTrackerWidgetNativeWindow>(window);
            Assert.Equal(1, fakeWindow.ClosedSubscriptionCount);
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

        private static WidgetSettings CreateCustomEnabledSettings()
        {
            return new WidgetSettings
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
            };
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
                add
                {
                    ClosedSubscriptionCount++;
                    _closed += value;
                }

                remove
                {
                    ClosedUnsubscriptionCount++;
                    _closed -= value;
                }
            }

            public int ActivateCallCount { get; private set; }

            public int CapturePlacementCallCount { get; private set; }

            public int ClosedSubscriptionCount { get; private set; }

            public int ClosedUnsubscriptionCount { get; private set; }

            public int CloseCallCount { get; private set; }

            public int ApplySettingsCallCount { get; private set; }

            public int UpdateTextCallCount { get; private set; }

            public string Text { get; private set; } = string.Empty;

            public WidgetSettings? AppliedSettings { get; private set; }

            public WidgetSettings? AppliedSettingsSnapshot { get; private set; }

            public Action? CapturePlacementCallback { get; set; }

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
                Text = text;
            }

            public void ApplySettings(WidgetSettings settings)
            {
                ApplySettingsCallCount++;
                AppliedSettings = settings;
                AppliedSettingsSnapshot = settings.Clone();
            }

            public void CapturePlacement(WidgetSettings settings)
            {
                CapturePlacementCallCount++;
                CapturePlacementCallback?.Invoke();
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
