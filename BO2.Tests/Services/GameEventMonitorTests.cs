using System;
using System.Buffers.Binary;
using System.Text;
using BO2.Services;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameEventMonitorTests
    {
        [Fact]
        public void DecodeSnapshot_WhenSnapshotIsValid_MapsKnownNotifyNames()
        {
            byte[] snapshot = CreateSnapshot(
                GameCompatibilityState.Compatible,
                droppedEventCount: 2,
                droppedNotifyCount: 3,
                publishedNotifyCount: 4,
                eventCount: 1);
            WriteEvent(snapshot, 0, GameEventType.Unknown, 12345, "start_of_round");
            DateTimeOffset receivedAt = new(2026, 4, 26, 1, 2, 3, TimeSpan.Zero);

            GameEventMonitorStatus status = GameEventMonitor.DecodeSnapshot(snapshot, receivedAt);

            Assert.Equal(GameCompatibilityState.Compatible, status.CompatibilityState);
            Assert.Equal(2u, status.DroppedEventCount);
            Assert.Equal(3u, status.DroppedNotifyCount);
            Assert.Equal(4u, status.PublishedNotifyCount);
            GameEvent gameEvent = Assert.Single(status.RecentEvents);
            Assert.Equal(GameEventType.StartOfRound, gameEvent.EventType);
            Assert.Equal("start_of_round", gameEvent.EventName);
            Assert.Equal(12345, gameEvent.LevelTime);
            Assert.Equal(7u, gameEvent.OwnerId);
            Assert.Equal(1149u, gameEvent.StringValue);
            Assert.Equal(receivedAt, gameEvent.ReceivedAt);
        }

        [Fact]
        public void DecodeSnapshot_WhenMagicDoesNotMatch_ReturnsUnsupportedVersion()
        {
            byte[] snapshot = CreateSnapshot(
                GameCompatibilityState.Compatible,
                droppedEventCount: 0,
                droppedNotifyCount: 0,
                publishedNotifyCount: 0,
                eventCount: 0);
            BinaryPrimitives.WriteUInt32LittleEndian(snapshot.AsSpan(0, 4), 0);

            GameEventMonitorStatus status = GameEventMonitor.DecodeSnapshot(snapshot, DateTimeOffset.UtcNow);

            Assert.Equal(GameCompatibilityState.UnsupportedVersion, status.CompatibilityState);
            Assert.Empty(status.RecentEvents);
        }

        [Fact]
        public void DecodeSnapshot_WhenVersionDoesNotMatch_ReturnsUnsupportedVersion()
        {
            byte[] snapshot = CreateSnapshot(
                GameCompatibilityState.Compatible,
                droppedEventCount: 0,
                droppedNotifyCount: 0,
                publishedNotifyCount: 0,
                eventCount: 0);
            BinaryPrimitives.WriteUInt32LittleEndian(snapshot.AsSpan(4, 4), 3);

            GameEventMonitorStatus status = GameEventMonitor.DecodeSnapshot(snapshot, DateTimeOffset.UtcNow);

            Assert.Equal(GameCompatibilityState.UnsupportedVersion, status.CompatibilityState);
            Assert.Empty(status.RecentEvents);
        }

        [Fact]
        public void DecodeSnapshot_WhenEventCountExceedsBuffer_CapsAtSharedMemoryCapacity()
        {
            byte[] snapshot = CreateSnapshot(
                GameCompatibilityState.Compatible,
                droppedEventCount: 0,
                droppedNotifyCount: 0,
                publishedNotifyCount: 0,
                eventCount: GameEventMonitor.MaxEventCount + 10);
            WriteEvent(snapshot, 0, GameEventType.PowerOn, 7, "power_on");
            WriteEvent(snapshot, GameEventMonitor.MaxEventCount - 1, GameEventType.EndGame, 8, "end_game");

            GameEventMonitorStatus status = GameEventMonitor.DecodeSnapshot(snapshot, DateTimeOffset.UtcNow);

            Assert.Equal(2, status.RecentEvents.Count);
            Assert.Equal(GameEventType.PowerOn, status.RecentEvents[0].EventType);
            Assert.Equal(GameEventType.EndGame, status.RecentEvents[^1].EventType);
        }

        [Theory]
        [InlineData("end_of_round", GameEventType.EndOfRound)]
        [InlineData("powerup_grabbed", GameEventType.PowerUpGrabbed)]
        [InlineData("dog_round_starting", GameEventType.DogRoundStarting)]
        [InlineData("perk_bought", GameEventType.PerkBought)]
        [InlineData("round_changed", GameEventType.RoundChanged)]
        [InlineData("points_changed", GameEventType.PointsChanged)]
        [InlineData("kills_changed", GameEventType.KillsChanged)]
        [InlineData("downs_changed", GameEventType.DownsChanged)]
        [InlineData("vm_notify_candidate_rejected", GameEventType.NotifyCandidateRejected)]
        [InlineData("vm_notify_entry_candidate", GameEventType.NotifyEntryCandidate)]
        [InlineData("sl_convert_candidate", GameEventType.StringResolverCandidate)]
        [InlineData("sl_get_string_of_size_candidate", GameEventType.StringResolverCandidate)]
        [InlineData("vm_notify_observed", GameEventType.NotifyObserved)]
        [InlineData("notify_log_opened", GameEventType.NotifyObserved)]
        [InlineData("weapon_bought", GameEventType.NotifyObserved)]
        [InlineData("zom_kill", GameEventType.NotifyObserved)]
        [InlineData("chest_accessed", GameEventType.BoxEvent)]
        [InlineData("user_grabbed_weapon", GameEventType.BoxEvent)]
        [InlineData("weapon_grabbed", GameEventType.BoxEvent)]
        [InlineData("randomization_done", GameEventType.BoxEvent)]
        [InlineData("box_moving", GameEventType.BoxEvent)]
        [InlineData("weapon_fly_away_start", GameEventType.BoxEvent)]
        [InlineData("weapon_fly_away_end", GameEventType.BoxEvent)]
        [InlineData("arrived", GameEventType.BoxEvent)]
        [InlineData("left", GameEventType.BoxEvent)]
        [InlineData("opened", GameEventType.BoxEvent)]
        [InlineData("closed", GameEventType.BoxEvent)]
        [InlineData("box_hacked_respin", GameEventType.BoxEvent)]
        [InlineData("box_hacked_rerespin", GameEventType.BoxEvent)]
        [InlineData("box_locked", GameEventType.BoxEvent)]
        [InlineData("locked", GameEventType.BoxEvent)]
        [InlineData("unlocked", GameEventType.BoxEvent)]
        [InlineData("box_spin_done", GameEventType.BoxEvent)]
        [InlineData("zbarrier_state_change", GameEventType.BoxEvent)]
        [InlineData("kill_chest_think", GameEventType.BoxEvent)]
        [InlineData("unregister_unitrigger_on_kill_think", GameEventType.BoxEvent)]
        [InlineData("lid_closed", GameEventType.BoxEvent)]
        [InlineData("kill_weapon_movement", GameEventType.BoxEvent)]
        [InlineData("kill_respin_think_thread", GameEventType.BoxEvent)]
        [InlineData("kill_respin_respin_think_thread", GameEventType.BoxEvent)]
        [InlineData("mb_hostmigration", GameEventType.BoxEvent)]
        [InlineData("stop_open_idle", GameEventType.BoxEvent)]
        [InlineData("unknown_notify", GameEventType.Unknown)]
        public void MapEventName_ReturnsExpectedEventType(string notifyName, GameEventType expected)
        {
            Assert.Equal(expected, GameEventMonitor.MapEventName(notifyName));
        }

        private static byte[] CreateSnapshot(
            GameCompatibilityState compatibilityState,
            uint droppedEventCount,
            uint droppedNotifyCount,
            uint publishedNotifyCount,
            uint eventCount)
        {
            byte[] snapshot = new byte[GameEventMonitor.SharedMemorySize];
            BinaryPrimitives.WriteUInt32LittleEndian(snapshot.AsSpan(0, 4), GameEventMonitor.SnapshotMagic);
            BinaryPrimitives.WriteUInt32LittleEndian(snapshot.AsSpan(4, 4), GameEventMonitor.SnapshotVersion);
            BinaryPrimitives.WriteInt32LittleEndian(snapshot.AsSpan(8, 4), (int)compatibilityState);
            BinaryPrimitives.WriteUInt32LittleEndian(snapshot.AsSpan(12, 4), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(snapshot.AsSpan(16, 4), droppedEventCount);
            BinaryPrimitives.WriteUInt32LittleEndian(snapshot.AsSpan(20, 4), eventCount);
            BinaryPrimitives.WriteUInt32LittleEndian(snapshot.AsSpan(24, 4), droppedNotifyCount);
            BinaryPrimitives.WriteUInt32LittleEndian(snapshot.AsSpan(28, 4), publishedNotifyCount);
            return snapshot;
        }

        private static void WriteEvent(
            byte[] snapshot,
            int index,
            GameEventType eventType,
            int levelTime,
            string eventName)
        {
            int offset = GameEventMonitor.HeaderSize + (index * GameEventMonitor.EventRecordSize);
            BinaryPrimitives.WriteInt32LittleEndian(snapshot.AsSpan(offset, 4), (int)eventType);
            BinaryPrimitives.WriteInt32LittleEndian(snapshot.AsSpan(offset + 4, 4), levelTime);
            BinaryPrimitives.WriteUInt32LittleEndian(snapshot.AsSpan(offset + 8, 4), 7);
            BinaryPrimitives.WriteUInt32LittleEndian(snapshot.AsSpan(offset + 12, 4), 1149);
            byte[] nameBytes = Encoding.UTF8.GetBytes(eventName);
            nameBytes.AsSpan(0, Math.Min(nameBytes.Length, GameEventMonitor.MaxEventNameBytes))
                .CopyTo(snapshot.AsSpan(offset + 16, GameEventMonitor.MaxEventNameBytes));
        }
    }
}
