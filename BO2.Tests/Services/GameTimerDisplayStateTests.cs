using System;
using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameTimerDisplayStateTests
    {
        [Theory]
        [InlineData(0, "0:00")]
        [InlineData(999, "0:00")]
        [InlineData(1_000, "0:01")]
        [InlineData(59_999, "0:59")]
        [InlineData(60_000, "1:00")]
        [InlineData(3_599_999, "59:59")]
        [InlineData(3_600_000, "1:00:00")]
        [InlineData(3_661_999, "1:01:01")]
        public void Format_FloorsMillisecondsToWholeSeconds(int elapsedMilliseconds, string expected)
        {
            string text = GameTimerDurationFormatter.Format(TimeSpan.FromMilliseconds(elapsedMilliseconds));

            Assert.Equal(expected, text);
        }

        [Fact]
        public void TimerDisplayState_RepresentsPlaceholderActiveAndFrozenTimerText()
        {
            Assert.Equal(TimerDisplayKind.Placeholder, TimerDisplayState.Placeholder.Kind);
            Assert.Equal(TimerDisplayState.PlaceholderText, Render(TimerDisplayState.Placeholder));
            Assert.Null(TimerDisplayState.Placeholder.Duration);

            TimerDisplayState active = TimerDisplayState.Active(TimeSpan.FromMilliseconds(61_999));
            TimerDisplayState frozen = TimerDisplayState.Frozen(TimeSpan.FromMilliseconds(125_999));

            Assert.Equal(TimerDisplayKind.Active, active.Kind);
            Assert.Equal("1:01", Render(active));
            Assert.Equal(TimeSpan.FromMilliseconds(61_999), active.Duration);
            Assert.Equal(TimerDisplayKind.Frozen, frozen.Kind);
            Assert.Equal("2:05", Render(frozen));
            Assert.Equal(TimeSpan.FromMilliseconds(125_999), frozen.Duration);
        }

        [Fact]
        public void TimerDisplayState_UsesSharedFormatterForGameAndRoundSlots()
        {
            TimeSpan elapsed = TimeSpan.FromMilliseconds(65_999);
            string expected = GameTimerDurationFormatter.Format(elapsed);
            GameConnectionTimerDisplayState timers = new(
                TimerDisplayState.Active(elapsed),
                TimerDisplayState.Frozen(elapsed));

            Assert.Equal(expected, Render(timers.GameTime));
            Assert.Equal(expected, Render(timers.RoundTime));
        }

        private static string Render(TimerDisplayState? timerDisplayState)
        {
            Assert.NotNull(timerDisplayState);
            TimerDisplayState timer = timerDisplayState!;
            return Assert.IsType<DisplayText.PlainText>(timer.Text).Text;
        }
    }
}
