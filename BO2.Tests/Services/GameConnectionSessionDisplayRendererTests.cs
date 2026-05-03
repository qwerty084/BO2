using System;
using System.Globalization;
using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameConnectionSessionDisplayRendererTests
    {
        [Fact]
        public void Render_WhenProjectionHasDefaults_ReturnsDefaultDisplayState()
        {
            GameConnectionSessionDisplayState state = CreateRenderer().Render(
                GameConnectionSessionDisplayProjection.CreateDefault());

            Assert.Equal(GameConnectionSessionDisplayState.EmptyStatText, state.PointsText);
            Assert.Equal("NoGameDetected", state.DetectedGameText);
            Assert.Equal("DllInjectionNotAttempted", state.InjectionStatusText);
            Assert.Equal("RecentEventsEmpty", state.BoxEventsText);
            Assert.Equal("GameNotRunning", state.StatusText);
            Assert.True(state.IsConnectButtonVisible);
            Assert.False(state.IsDisconnectButtonEnabled);
            Assert.True(state.IsFooterDisconnectedStatusVisible);
        }

        [Fact]
        public void Render_WhenTextIsResource_ReturnsResourceValue()
        {
            string text = CreateRenderer().Render(DisplayText.Resource("NoGameDetected"));

            Assert.Equal("NoGameDetected", text);
        }

        [Fact]
        public void Render_WhenTextIsFormat_RendersNestedArguments()
        {
            string text = CreateRenderer().Render(DisplayText.Format(
                "OuterFormat",
                DisplayText.Resource("InnerResource"),
                DisplayText.Format("ChildFormat", DisplayText.Plain("value"))));

            Assert.Equal("OuterFormat(InnerResource, ChildFormat(value))", text);
        }

        [Fact]
        public void Render_WhenTextIsLines_JoinsRenderedLines()
        {
            string text = CreateRenderer().Render(DisplayText.Lines(
                DisplayText.Plain("first"),
                DisplayText.Resource("SecondLine")));

            Assert.Equal($"first{Environment.NewLine}SecondLine", text);
        }

        [Fact]
        public void Render_WhenTextIsInteger_UsesCurrentCultureStatFormat()
        {
            string text = CreateRenderer().Render(DisplayText.Integer(1234));

            Assert.Equal(1234.ToString("N0", CultureInfo.CurrentCulture), text);
        }

        [Fact]
        public void Render_WhenTextIsFloat2_UsesCurrentCultureCoordinateFormat()
        {
            string text = CreateRenderer().Render(DisplayText.Float2(12.345f));

            Assert.Equal(12.345f.ToString("N2", CultureInfo.CurrentCulture), text);
        }

        [Fact]
        public void Render_WhenTextIsAddress_UsesHexAddressFormat()
        {
            string text = CreateRenderer().Render(DisplayText.Address(0x1234ABCD));

            Assert.Equal("0x1234ABCD", text);
        }

        [Fact]
        public void Render_WhenTextIsLocalTime_UsesCurrentLocalClockFormat()
        {
            DateTimeOffset receivedAt = new(2026, 5, 2, 12, 34, 56, TimeSpan.Zero);

            string text = CreateRenderer().Render(DisplayText.LocalTime(receivedAt));

            Assert.Equal(receivedAt.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture), text);
        }

        private static GameConnectionSessionDisplayRenderer CreateRenderer()
        {
            return new GameConnectionSessionDisplayRenderer();
        }
    }
}
