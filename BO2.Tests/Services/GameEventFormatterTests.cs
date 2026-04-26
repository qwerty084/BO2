using System;
using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameEventFormatterTests
    {
        [Fact]
        public void FormatRecentBoxEvents_WhenWeaponNameIsResolved_IncludesDisplayNameAndAlias()
        {
            DateTimeOffset receivedAt = new(2026, 4, 26, 12, 34, 56, TimeSpan.Zero);
            GameEventMonitorStatus status = new(
                GameCompatibilityState.Compatible,
                0,
                0,
                1,
                new[]
                {
                    new GameEvent(GameEventType.BoxEvent, "randomization_done", 0, 7, 1149, receivedAt, "fnfal_zm")
                });

            string text = GameEventFormatter.FormatRecentBoxEvents(status);

            Assert.Contains("BoxEventWithWeaponFormat", text);
            Assert.Contains("randomization_done", text);
            Assert.Contains("FAL (fnfal_zm)", text);
            Assert.Contains("7", text);
            Assert.Contains("1149", text);
        }

        [Fact]
        public void FormatRecentBoxEvents_WhenWeaponNameIsUnresolved_IncludesAlias()
        {
            DateTimeOffset receivedAt = new(2026, 4, 26, 12, 34, 56, TimeSpan.Zero);
            GameEventMonitorStatus status = new(
                GameCompatibilityState.Compatible,
                0,
                0,
                1,
                new[]
                {
                    new GameEvent(GameEventType.BoxEvent, "randomization_done", 0, 7, 1149, receivedAt, "unknown_weapon_zm")
                });

            string text = GameEventFormatter.FormatRecentBoxEvents(status);

            Assert.Contains("BoxEventWithWeaponFormat", text);
            Assert.Contains("randomization_done", text);
            Assert.Contains("unknown_weapon_zm", text);
            Assert.Contains("7", text);
            Assert.Contains("1149", text);
        }

        [Fact]
        public void FormatRecentBoxEvents_WhenWeaponNameIsMissing_UsesLegacyBoxFormat()
        {
            DateTimeOffset receivedAt = new(2026, 4, 26, 12, 34, 56, TimeSpan.Zero);
            GameEventMonitorStatus status = new(
                GameCompatibilityState.Compatible,
                0,
                0,
                1,
                new[]
                {
                    new GameEvent(GameEventType.BoxEvent, "chest_accessed", 0, 7, 1149, receivedAt)
                });

            string text = GameEventFormatter.FormatRecentBoxEvents(status);

            Assert.Contains("BoxEventFormat", text);
            Assert.DoesNotContain("BoxEventWithWeaponFormat", text);
            Assert.Contains("chest_accessed", text);
        }
    }
}
