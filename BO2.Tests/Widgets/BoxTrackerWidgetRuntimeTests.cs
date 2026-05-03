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
        public void EnsureNativeWindow_CreatesWindowThroughAdapter()
        {
            var adapter = new FakeBoxTrackerWidgetNativeAdapter();
            var runtime = new BoxTrackerWidgetRuntime(adapter);

            IBoxTrackerWidgetNativeWindow window = runtime.EnsureNativeWindow();

            Assert.True(runtime.HasNativeWindow);
            Assert.Same(adapter.CreatedWindow, window);
            Assert.Equal(1, adapter.CreateWindowCallCount);
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

            public void Activate()
            {
            }

            public void Close()
            {
                Closed?.Invoke(this, EventArgs.Empty);
            }

            public void UpdateText(string text)
            {
            }

            public void ApplySettings(WidgetSettings settings)
            {
            }

            public void CapturePlacement(WidgetSettings settings)
            {
            }
        }
    }
}
