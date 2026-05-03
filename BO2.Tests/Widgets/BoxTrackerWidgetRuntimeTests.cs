using System;
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

            public int UpdateTextCallCount { get; private set; }

            public string Text { get; private set; } = string.Empty;

            public WidgetSettings? AppliedSettings { get; private set; }

            public void Activate()
            {
                ActivateCallCount++;
            }

            public void Close()
            {
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
            }
        }
    }
}
