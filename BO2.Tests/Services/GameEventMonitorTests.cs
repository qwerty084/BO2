using System;
using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;
using BO2.Services;
using BO2.Services.Generated;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameEventMonitorTests
    {
        private static int _nextMonitorTestProcessId = 1_400_000_000;

        [Fact]
        public void DecodeSnapshot_WhenSnapshotIsValid_MapsKnownNotifyNames()
        {
            byte[] snapshot = CreateSnapshot(
                GameCompatibilityState.Compatible,
                droppedEventCount: 2,
                droppedNotifyCount: 3,
                publishedNotifyCount: 4,
                eventCount: 1);
            DateTimeOffset receivedAt = new(2026, 4, 26, 1, 2, 3, TimeSpan.Zero);
            const uint receivedAtTick = 1000;
            WriteEvent(snapshot, 0, GameEventType.Unknown, 12345, "start_of_round", receivedAtTick);

            GameEventMonitorStatus status = GameEventMonitor.DecodeSnapshot(snapshot, receivedAt, receivedAtTick);

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
            Assert.Null(gameEvent.WeaponName);
        }

        [Fact]
        public void DecodeSnapshot_WhenWeaponNameIsPresent_DecodesWeaponName()
        {
            byte[] snapshot = CreateSnapshot(
                GameCompatibilityState.Compatible,
                droppedEventCount: 0,
                droppedNotifyCount: 0,
                publishedNotifyCount: 1,
                eventCount: 1);
            WriteEvent(snapshot, 0, GameEventType.BoxEvent, 0, "randomization_done", weaponName: "ray_gun_zm");

            GameEventMonitorStatus status = GameEventMonitor.DecodeSnapshot(snapshot, DateTimeOffset.UtcNow);

            GameEvent gameEvent = Assert.Single(status.RecentEvents);
            Assert.Equal("ray_gun_zm", gameEvent.WeaponName);
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
            BinaryPrimitives.WriteUInt32LittleEndian(
                snapshot.AsSpan(
                    EventMonitorSnapshotContract.SharedSnapshotMagicOffset,
                    EventMonitorSnapshotContract.SharedSnapshotMagicSize),
                0);

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
            BinaryPrimitives.WriteUInt32LittleEndian(
                snapshot.AsSpan(
                    EventMonitorSnapshotContract.SharedSnapshotVersionOffset,
                    EventMonitorSnapshotContract.SharedSnapshotVersionSize),
                3);

            GameEventMonitorStatus status = GameEventMonitor.DecodeSnapshot(snapshot, DateTimeOffset.UtcNow);

            Assert.Equal(GameCompatibilityState.UnsupportedVersion, status.CompatibilityState);
            Assert.Empty(status.RecentEvents);
        }

        [Fact]
        public void DecodeSnapshot_WhenSnapshotIsVersionFive_ReturnsUnsupportedVersion()
        {
            byte[] snapshot = CreateSnapshot(
                GameCompatibilityState.Compatible,
                droppedEventCount: 0,
                droppedNotifyCount: 0,
                publishedNotifyCount: 0,
                eventCount: 0);
            BinaryPrimitives.WriteUInt32LittleEndian(
                snapshot.AsSpan(
                    EventMonitorSnapshotContract.SharedSnapshotVersionOffset,
                    EventMonitorSnapshotContract.SharedSnapshotVersionSize),
                5);

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
            WriteEvent(snapshot, 0, GameEventType.StartOfRound, 7, "start_of_round");
            WriteEvent(snapshot, GameEventMonitor.MaxEventCount - 1, GameEventType.EndGame, 8, "end_game");

            GameEventMonitorStatus status = GameEventMonitor.DecodeSnapshot(snapshot, DateTimeOffset.UtcNow);

            Assert.Equal(2, status.RecentEvents.Count);
            Assert.Equal(GameEventType.StartOfRound, status.RecentEvents[0].EventType);
            Assert.Equal(GameEventType.EndGame, status.RecentEvents[^1].EventType);
        }

        [Fact]
        public void DecodeSnapshot_WhenRingHasNotWrapped_ReturnsEventsOldestToNewest()
        {
            byte[] snapshot = CreateSnapshot(
                GameCompatibilityState.Compatible,
                droppedEventCount: 0,
                droppedNotifyCount: 0,
                publishedNotifyCount: 3,
                eventCount: 3,
                eventWriteIndex: 3);
            WriteEvent(snapshot, 0, GameEventType.NotifyObserved, 10, "first");
            WriteEvent(snapshot, 1, GameEventType.NotifyObserved, 20, "second");
            WriteEvent(snapshot, 2, GameEventType.NotifyObserved, 30, "third");

            GameEventMonitorStatus status = GameEventMonitor.DecodeSnapshot(snapshot, DateTimeOffset.UtcNow);

            Assert.Equal(3, status.RecentEvents.Count);
            Assert.Equal("first", status.RecentEvents[0].EventName);
            Assert.Equal("second", status.RecentEvents[1].EventName);
            Assert.Equal("third", status.RecentEvents[2].EventName);
        }

        [Fact]
        public void DecodeSnapshot_WhenRingHasWrapped_ReturnsEventsOldestToNewestFromWriteIndex()
        {
            byte[] snapshot = CreateSnapshot(
                GameCompatibilityState.Compatible,
                droppedEventCount: 2,
                droppedNotifyCount: 0,
                publishedNotifyCount: 0,
                eventCount: GameEventMonitor.MaxEventCount,
                eventWriteIndex: 2);
            for (int eventNumber = 2; eventNumber < GameEventMonitor.MaxEventCount; eventNumber++)
            {
                WriteEvent(
                    snapshot,
                    eventNumber,
                    GameEventType.NotifyObserved,
                    eventNumber,
                    $"event_{eventNumber}");
            }

            WriteEvent(
                snapshot,
                0,
                GameEventType.NotifyObserved,
                GameEventMonitor.MaxEventCount,
                $"event_{GameEventMonitor.MaxEventCount}");
            WriteEvent(
                snapshot,
                1,
                GameEventType.NotifyObserved,
                GameEventMonitor.MaxEventCount + 1,
                $"event_{GameEventMonitor.MaxEventCount + 1}");

            GameEventMonitorStatus status = GameEventMonitor.DecodeSnapshot(snapshot, DateTimeOffset.UtcNow);

            Assert.Equal(GameEventMonitor.MaxEventCount, status.RecentEvents.Count);
            for (int index = 0; index < status.RecentEvents.Count; index++)
            {
                int expectedEventNumber = index + 2;
                Assert.Equal(expectedEventNumber, status.RecentEvents[index].LevelTime);
                Assert.Equal($"event_{expectedEventNumber}", status.RecentEvents[index].EventName);
            }
        }

        [Fact]
        public void TryReadStableSnapshot_WhenWriteSequenceStaysOdd_RefusesSnapshot()
        {
            var reader = new ScriptedStableSnapshotReader([1, 3, 5, 7]);
            byte[] snapshot = new byte[GameEventMonitor.SharedMemorySize];

            bool wasStable = GameEventMonitor.TryReadStableSnapshot(reader, snapshot);

            Assert.False(wasStable);
            Assert.Equal(0, reader.SnapshotReadCount);
        }

        [Fact]
        public void TryReadStableSnapshot_WhenWriteSequenceChangesDuringRead_RefusesSnapshot()
        {
            var reader = new ScriptedStableSnapshotReader([2, 4, 6, 8, 10, 12, 14, 16]);
            byte[] snapshot = new byte[GameEventMonitor.SharedMemorySize];

            bool wasStable = GameEventMonitor.TryReadStableSnapshot(reader, snapshot);

            Assert.False(wasStable);
            Assert.Equal(4, reader.SnapshotReadCount);
        }

        [Theory]
        [InlineData("start_of_round", GameEventType.StartOfRound)]
        [InlineData("end_of_round", GameEventType.EndOfRound)]
        [InlineData("end_game", GameEventType.EndGame)]
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
        [InlineData("randomization_done", GameEventType.BoxEvent)]
        [InlineData("user_grabbed_weapon", GameEventType.BoxEvent)]
        [InlineData("chest_accessed", GameEventType.BoxEvent)]
        [InlineData("box_moving", GameEventType.BoxEvent)]
        [InlineData("weapon_fly_away_start", GameEventType.BoxEvent)]
        [InlineData("weapon_fly_away_end", GameEventType.BoxEvent)]
        [InlineData("arrived", GameEventType.BoxEvent)]
        [InlineData("left", GameEventType.BoxEvent)]
        [InlineData("closed", GameEventType.BoxEvent)]
        [InlineData("unknown_notify", GameEventType.Unknown)]
        public void MapEventName_ReturnsExpectedEventType(string notifyName, GameEventType expected)
        {
            Assert.Equal(expected, GameEventMonitor.MapEventName(notifyName));
        }

        [Theory]
        [InlineData("powerup_grabbed")]
        [InlineData("dog_round_starting")]
        [InlineData("power_on")]
        [InlineData("perk_bought")]
        [InlineData("weapon_bought")]
        [InlineData("zom_kill")]
        [InlineData("weapon_grabbed")]
        [InlineData("box_spin_done")]
        [InlineData("box_hacked_respin")]
        [InlineData("box_hacked_rerespin")]
        [InlineData("box_locked")]
        [InlineData("locked")]
        [InlineData("unlocked")]
        [InlineData("zbarrier_state_change")]
        [InlineData("kill_chest_think")]
        [InlineData("unregister_unitrigger_on_kill_think")]
        [InlineData("lid_closed")]
        [InlineData("kill_weapon_movement")]
        [InlineData("kill_respin_think_thread")]
        [InlineData("kill_respin_respin_think_thread")]
        [InlineData("mb_hostmigration")]
        [InlineData("stop_open_idle")]
        public void MapEventName_WhenNotifyWasRemoved_ReturnsUnknown(string notifyName)
        {
            Assert.Equal(GameEventType.Unknown, GameEventMonitor.MapEventName(notifyName));
        }

        [Fact]
        public void BuildSharedMemoryName_UsesTargetProcessId()
        {
            Assert.Equal(EventMonitorSnapshotContract.SharedMemoryNamePrefix + "1234", GameEventMonitor.BuildSharedMemoryName(1234));
        }

        [Fact]
        public void BuildEventHandleName_UsesTargetProcessId()
        {
            Assert.Equal(EventMonitorSnapshotContract.UpdateEventNamePrefix + "1234", GameEventMonitor.BuildEventHandleName(1234));
        }

        [Fact]
        public void BuildStopEventHandleName_UsesTargetProcessId()
        {
            Assert.Equal(EventMonitorSnapshotContract.StopEventNamePrefix + "1234", GameEventMonitor.BuildStopEventHandleName(1234));
        }

        [Fact]
        public void RequestStop_WhenStopEventExists_SignalsStopEvent()
        {
            int processId = NextMonitorTestProcessId();
            using EventWaitHandle stopEvent = new(false, EventResetMode.ManualReset, GameEventMonitor.BuildStopEventHandleName(processId));
            stopEvent.Reset();
            using var monitor = new GameEventMonitor();

            monitor.RequestStop(processId);

            Assert.True(stopEvent.WaitOne(0));
        }

        [Fact]
        public void IsStopComplete_WhenStopEventExists_ReturnsFalse()
        {
            int processId = NextMonitorTestProcessId();
            using EventWaitHandle stopEvent = new(false, EventResetMode.ManualReset, GameEventMonitor.BuildStopEventHandleName(processId));
            using var monitor = new GameEventMonitor();

            Assert.False(monitor.IsStopComplete(processId));
        }

        [Fact]
        public void IsStopComplete_WhenStopEventCloses_ReturnsTrue()
        {
            int processId = NextMonitorTestProcessId();
            using EventWaitHandle stopEvent = new(false, EventResetMode.ManualReset, GameEventMonitor.BuildStopEventHandleName(processId));
            using var monitor = new GameEventMonitor();
            Assert.False(monitor.IsStopComplete(processId));

            stopEvent.Dispose();

            Assert.True(monitor.IsStopComplete(processId));
        }

        [Fact]
        public void ReadStatus_WhenReadinessSignalIsMissing_ReturnsWaitingForMonitor()
        {
            int processId = NextMonitorTestProcessId();
            using EventWaitHandle eventHandle = new(false, EventResetMode.ManualReset, GameEventMonitor.BuildEventHandleName(processId));
            using EventWaitHandle stopEvent = new(false, EventResetMode.ManualReset, GameEventMonitor.BuildStopEventHandleName(processId));
            using MemoryMappedFile sharedMemory = MemoryMappedFile.CreateNew(
                GameEventMonitor.BuildSharedMemoryName(processId),
                GameEventMonitor.SharedMemorySize);
            byte[] snapshot = CreateSnapshot(
                GameCompatibilityState.Compatible,
                droppedEventCount: 0,
                droppedNotifyCount: 0,
                publishedNotifyCount: 1,
                eventCount: 1);
            WriteEvent(snapshot, 0, GameEventType.BoxEvent, 12, "randomization_done", weaponName: "ray_gun_zm");
            WriteSnapshotToSharedMemory(sharedMemory, snapshot);
            using var monitor = new GameEventMonitor();

            GameEventMonitorStatus status = monitor.ReadStatus(DateTimeOffset.UtcNow, processId);

            Assert.Equal(GameCompatibilityState.WaitingForMonitor, status.CompatibilityState);
            Assert.Empty(status.RecentEvents);
        }

        [Fact]
        public void ReadStatus_WhenReadinessSignalIsObserved_DecodesSnapshot()
        {
            int processId = NextMonitorTestProcessId();
            using EventWaitHandle eventHandle = new(false, EventResetMode.ManualReset, GameEventMonitor.BuildEventHandleName(processId));
            using EventWaitHandle stopEvent = new(false, EventResetMode.ManualReset, GameEventMonitor.BuildStopEventHandleName(processId));
            using MemoryMappedFile sharedMemory = MemoryMappedFile.CreateNew(
                GameEventMonitor.BuildSharedMemoryName(processId),
                GameEventMonitor.SharedMemorySize);
            byte[] snapshot = CreateSnapshot(
                GameCompatibilityState.Compatible,
                droppedEventCount: 0,
                droppedNotifyCount: 0,
                publishedNotifyCount: 1,
                eventCount: 1);
            WriteEvent(snapshot, 0, GameEventType.BoxEvent, 12, "randomization_done", weaponName: "ray_gun_zm");
            WriteSnapshotToSharedMemory(sharedMemory, snapshot);
            eventHandle.Set();
            using var monitor = new GameEventMonitor();

            GameEventMonitorStatus status = monitor.ReadStatus(DateTimeOffset.UtcNow, processId);

            Assert.Equal(GameCompatibilityState.Compatible, status.CompatibilityState);
            GameEvent gameEvent = Assert.Single(status.RecentEvents);
            Assert.Equal(GameEventType.BoxEvent, gameEvent.EventType);
            Assert.Equal("randomization_done", gameEvent.EventName);
            Assert.Equal("ray_gun_zm", gameEvent.WeaponName);
        }

        [Fact]
        public void DecodeSnapshot_UsesNativeTickForStableEventTimestamp()
        {
            byte[] snapshot = CreateSnapshot(
                GameCompatibilityState.Compatible,
                droppedEventCount: 0,
                droppedNotifyCount: 0,
                publishedNotifyCount: 1,
                eventCount: 1);
            WriteEvent(snapshot, 0, GameEventType.StartOfRound, 7, "start_of_round", tick: 98_000);
            DateTimeOffset firstReadAt = new(2026, 4, 26, 1, 2, 3, TimeSpan.Zero);
            DateTimeOffset secondReadAt = firstReadAt.AddSeconds(1);

            GameEventMonitorStatus firstStatus = GameEventMonitor.DecodeSnapshot(snapshot, firstReadAt, receivedAtTick: 100_000);
            GameEventMonitorStatus secondStatus = GameEventMonitor.DecodeSnapshot(snapshot, secondReadAt, receivedAtTick: 101_000);

            Assert.Equal(firstReadAt.AddSeconds(-2), firstStatus.RecentEvents[0].ReceivedAt);
            Assert.Equal(firstStatus.RecentEvents[0].ReceivedAt, secondStatus.RecentEvents[0].ReceivedAt);
        }

        private static byte[] CreateSnapshot(
            GameCompatibilityState compatibilityState,
            uint droppedEventCount,
            uint droppedNotifyCount,
            uint publishedNotifyCount,
            uint eventCount,
            uint eventWriteIndex = 0,
            uint writeSequence = 0)
        {
            byte[] snapshot = new byte[GameEventMonitor.SharedMemorySize];
            BinaryPrimitives.WriteUInt32LittleEndian(
                snapshot.AsSpan(
                    EventMonitorSnapshotContract.SharedSnapshotMagicOffset,
                    EventMonitorSnapshotContract.SharedSnapshotMagicSize),
                GameEventMonitor.SnapshotMagic);
            BinaryPrimitives.WriteUInt32LittleEndian(
                snapshot.AsSpan(
                    EventMonitorSnapshotContract.SharedSnapshotVersionOffset,
                    EventMonitorSnapshotContract.SharedSnapshotVersionSize),
                GameEventMonitor.SnapshotVersion);
            BinaryPrimitives.WriteInt32LittleEndian(
                snapshot.AsSpan(
                    EventMonitorSnapshotContract.SharedSnapshotCompatibilityStateOffset,
                    EventMonitorSnapshotContract.SharedSnapshotCompatibilityStateSize),
                (int)compatibilityState);
            BinaryPrimitives.WriteUInt32LittleEndian(
                snapshot.AsSpan(
                    EventMonitorSnapshotContract.SharedSnapshotEventWriteIndexOffset,
                    EventMonitorSnapshotContract.SharedSnapshotEventWriteIndexSize),
                eventWriteIndex);
            BinaryPrimitives.WriteUInt32LittleEndian(
                snapshot.AsSpan(
                    EventMonitorSnapshotContract.SharedSnapshotDroppedEventCountOffset,
                    EventMonitorSnapshotContract.SharedSnapshotDroppedEventCountSize),
                droppedEventCount);
            BinaryPrimitives.WriteUInt32LittleEndian(
                snapshot.AsSpan(
                    EventMonitorSnapshotContract.SharedSnapshotEventCountOffset,
                    EventMonitorSnapshotContract.SharedSnapshotEventCountSize),
                eventCount);
            BinaryPrimitives.WriteUInt32LittleEndian(
                snapshot.AsSpan(
                    EventMonitorSnapshotContract.SharedSnapshotDroppedNotifyCountOffset,
                    EventMonitorSnapshotContract.SharedSnapshotDroppedNotifyCountSize),
                droppedNotifyCount);
            BinaryPrimitives.WriteUInt32LittleEndian(
                snapshot.AsSpan(
                    EventMonitorSnapshotContract.SharedSnapshotPublishedNotifyCountOffset,
                    EventMonitorSnapshotContract.SharedSnapshotPublishedNotifyCountSize),
                publishedNotifyCount);
            BinaryPrimitives.WriteUInt32LittleEndian(
                snapshot.AsSpan(
                    EventMonitorSnapshotContract.SharedSnapshotWriteSequenceOffset,
                    EventMonitorSnapshotContract.SharedSnapshotWriteSequenceSize),
                writeSequence);
            return snapshot;
        }

        private static void WriteEvent(
            byte[] snapshot,
            int index,
            GameEventType eventType,
            int levelTime,
            string eventName,
            uint tick = 1000,
            string? weaponName = null)
        {
            int offset = EventMonitorSnapshotContract.SharedSnapshotEventsOffset + (index * GameEventMonitor.EventRecordSize);
            BinaryPrimitives.WriteInt32LittleEndian(
                snapshot.AsSpan(
                    offset + EventMonitorSnapshotContract.GameEventRecordEventTypeOffset,
                    EventMonitorSnapshotContract.GameEventRecordEventTypeSize),
                (int)eventType);
            BinaryPrimitives.WriteInt32LittleEndian(
                snapshot.AsSpan(
                    offset + EventMonitorSnapshotContract.GameEventRecordLevelTimeOffset,
                    EventMonitorSnapshotContract.GameEventRecordLevelTimeSize),
                levelTime);
            BinaryPrimitives.WriteUInt32LittleEndian(
                snapshot.AsSpan(
                    offset + EventMonitorSnapshotContract.GameEventRecordOwnerIdOffset,
                    EventMonitorSnapshotContract.GameEventRecordOwnerIdSize),
                7);
            BinaryPrimitives.WriteUInt32LittleEndian(
                snapshot.AsSpan(
                    offset + EventMonitorSnapshotContract.GameEventRecordStringValueOffset,
                    EventMonitorSnapshotContract.GameEventRecordStringValueSize),
                1149);
            BinaryPrimitives.WriteUInt32LittleEndian(
                snapshot.AsSpan(
                    offset + EventMonitorSnapshotContract.GameEventRecordTickOffset,
                    EventMonitorSnapshotContract.GameEventRecordTickSize),
                tick);
            byte[] nameBytes = Encoding.UTF8.GetBytes(eventName);
            nameBytes.AsSpan(0, Math.Min(nameBytes.Length, GameEventMonitor.MaxEventNameBytes))
                .CopyTo(snapshot.AsSpan(offset + GameEventMonitor.EventNameOffset, GameEventMonitor.MaxEventNameBytes));

            if (weaponName is not null)
            {
                byte[] weaponNameBytes = Encoding.UTF8.GetBytes(weaponName);
                weaponNameBytes.AsSpan(0, Math.Min(weaponNameBytes.Length, GameEventMonitor.MaxWeaponNameBytes))
                    .CopyTo(snapshot.AsSpan(offset + GameEventMonitor.WeaponNameOffset, GameEventMonitor.MaxWeaponNameBytes));
            }
        }

        private static void WriteSnapshotToSharedMemory(MemoryMappedFile sharedMemory, byte[] snapshot)
        {
            using MemoryMappedViewAccessor accessor = sharedMemory.CreateViewAccessor(
                0,
                GameEventMonitor.SharedMemorySize,
                MemoryMappedFileAccess.Write);
            accessor.WriteArray(0, snapshot, 0, snapshot.Length);
        }

        private static int NextMonitorTestProcessId()
        {
            return Interlocked.Increment(ref _nextMonitorTestProcessId);
        }

        private sealed class ScriptedStableSnapshotReader(uint[] sequences) : GameEventMonitor.IStableSnapshotReader
        {
            private int _sequenceIndex;

            public int SnapshotReadCount { get; private set; }

            public void ReadWriteSequence(out uint sequence)
            {
                sequence = sequences[_sequenceIndex++];
            }

            public void ReadSnapshot(byte[] snapshot)
            {
                SnapshotReadCount++;
            }
        }
    }
}
