using System;
using System.Globalization;

namespace BO2.Services
{
    internal enum TimerDisplayKind
    {
        Placeholder,
        Active,
        Frozen
    }

    internal sealed record TimerDisplayState
    {
        public const string PlaceholderText = "--:--";

        private TimerDisplayState(TimerDisplayKind kind, DisplayText text)
        {
            ArgumentNullException.ThrowIfNull(text);

            Kind = kind;
            Text = text;
        }

        public TimerDisplayKind Kind { get; }

        public DisplayText Text { get; }

        public static TimerDisplayState Placeholder { get; } = new(
            TimerDisplayKind.Placeholder,
            DisplayText.Plain(PlaceholderText));

        public static TimerDisplayState Active(TimeSpan elapsed)
        {
            return FromDuration(TimerDisplayKind.Active, elapsed);
        }

        public static TimerDisplayState Frozen(TimeSpan elapsed)
        {
            return FromDuration(TimerDisplayKind.Frozen, elapsed);
        }

        private static TimerDisplayState FromDuration(TimerDisplayKind kind, TimeSpan elapsed)
        {
            return new TimerDisplayState(
                kind,
                DisplayText.Plain(GameTimerDurationFormatter.Format(elapsed)));
        }
    }

    internal sealed record GameConnectionTimerDisplayState(
        TimerDisplayState? GameTime,
        TimerDisplayState? RoundTime)
    {
        public static GameConnectionTimerDisplayState Placeholder { get; } = new(
            TimerDisplayState.Placeholder,
            TimerDisplayState.Placeholder);
    }

    internal static class GameTimerDurationFormatter
    {
        public static string Format(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(elapsed), "Timer durations cannot be negative.");
            }

            long totalSeconds = (long)Math.Floor(elapsed.TotalMilliseconds / 1000d);
            long hours = totalSeconds / 3600;
            long minutes = (totalSeconds % 3600) / 60;
            long seconds = totalSeconds % 60;

            return hours > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds:00}")
                : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds:00}");
        }
    }
}
