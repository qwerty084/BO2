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

        [Fact]
        public void FormatRecentGameEvents_WhenDiagnosticsArePresent_HidesDiagnostics()
        {
            DateTimeOffset receivedAt = new(2026, 4, 26, 12, 34, 56, TimeSpan.Zero);
            GameEventMonitorStatus status = new(
                GameCompatibilityState.Compatible,
                0,
                0,
                4,
                new[]
                {
                    new GameEvent(GameEventType.NotifyCandidateRejected, "vm_notify_candidate_rejected", 1, 0, 9385504, receivedAt),
                    new GameEvent(GameEventType.NotifyEntryCandidate, "vm_notify_entry_candidate", 1, 0, 9384400, receivedAt),
                    new GameEvent(GameEventType.StringResolverCandidate, "sl_string_data_candidate", 1, 0, 46105508, receivedAt),
                    new GameEvent(GameEventType.StartOfRound, "start_of_round", 1, 3, 417, receivedAt),
                    new GameEvent(GameEventType.BoxEvent, "randomization_done", 0, 4780, 422, receivedAt, "fnfal_zm")
                });

            string text = GameEventFormatter.FormatRecentGameEvents(status);

            Assert.DoesNotContain("vm_notify_candidate_rejected", text);
            Assert.DoesNotContain("vm_notify_entry_candidate", text);
            Assert.DoesNotContain("sl_string_data_candidate", text);
            Assert.Contains("start_of_round", text);
            Assert.Contains("randomization_done", text);
        }

        [Fact]
        public void FormatRecentGameEvents_WhenOnlyDiagnosticsArePresent_ReturnsEmptyText()
        {
            DateTimeOffset receivedAt = new(2026, 4, 26, 12, 34, 56, TimeSpan.Zero);
            GameEventMonitorStatus status = new(
                GameCompatibilityState.Compatible,
                0,
                0,
                3,
                new[]
                {
                    new GameEvent(GameEventType.NotifyCandidateRejected, "vm_notify_candidate_rejected", 1, 0, 9385504, receivedAt),
                    new GameEvent(GameEventType.NotifyEntryCandidate, "vm_notify_entry_candidate", 1, 0, 9384400, receivedAt),
                    new GameEvent(GameEventType.StringResolverCandidate, "sl_string_data_candidate", 1, 0, 46105508, receivedAt)
                });

            string text = GameEventFormatter.FormatRecentGameEvents(status);

            Assert.Equal("RecentEventsEmpty", text);
        }

        [Fact]
        public void FormatBoxTrackerEvents_WhenNoBoxEvents_ReturnsWidgetPrompt()
        {
            GameEventMonitorStatus status = new(
                GameCompatibilityState.Compatible,
                0,
                0,
                0,
                Array.Empty<GameEvent>());

            string text = GameEventFormatter.FormatBoxTrackerEvents(status);

            Assert.Equal("BoxTrackerEmpty", text);
        }
    }
}
