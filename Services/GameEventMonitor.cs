using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;
using BO2.Services.Generated;

namespace BO2.Services
{
    public sealed class GameEventMonitor : IGameEventMonitor
    {
        public const string SharedMemoryNamePrefix = EventMonitorSnapshotContract.SharedMemoryNamePrefix;
        public const string EventHandleNamePrefix = EventMonitorSnapshotContract.UpdateEventNamePrefix;
        public const string StopEventHandleNamePrefix = EventMonitorSnapshotContract.StopEventNamePrefix;

        internal const uint SnapshotMagic = EventMonitorSnapshotContract.SnapshotMagic;
        internal const uint SnapshotVersion = EventMonitorSnapshotContract.SnapshotVersion;
        internal const uint MinimumSupportedSnapshotVersion = EventMonitorSnapshotContract.MinimumSupportedSnapshotVersion;
        internal const uint MaximumSupportedSnapshotVersion = EventMonitorSnapshotContract.MaximumSupportedSnapshotVersion;
        internal const int MaxEventCount = EventMonitorSnapshotContract.MaxEventCount;
        internal const int MaxEventNameBytes = EventMonitorSnapshotContract.MaxEventNameBytes;
        internal const int MaxWeaponNameBytes = EventMonitorSnapshotContract.MaxWeaponNameBytes;
        internal const int HeaderSize = EventMonitorSnapshotContract.HeaderSize;
        internal const int EventRecordSize = EventMonitorSnapshotContract.EventRecordSize;
        internal const int EventNameOffset = EventMonitorSnapshotContract.GameEventRecordEventNameOffset;
        internal const int WeaponNameOffset = EventMonitorSnapshotContract.GameEventRecordWeaponNameOffset;
        internal const int SharedMemorySize = EventMonitorSnapshotContract.SharedMemorySize;
        internal const int WriteSequenceOffset = EventMonitorSnapshotContract.SharedSnapshotWriteSequenceOffset;
        private const int StableReadAttempts = 4;

        private int? _targetProcessId;
        private MemoryMappedFile? _sharedMemory;
        private EventWaitHandle? _eventHandle;
        private EventWaitHandle? _stopEventHandle;
        private bool _readinessSignalObserved;

        internal interface IStableSnapshotReader
        {
            void ReadWriteSequence(out uint sequence);

            void ReadSnapshot(byte[] snapshot);
        }

        public GameEventMonitorStatus ReadStatus(DateTimeOffset receivedAt, int? targetProcessId)
        {
            if (targetProcessId is null or <= 0)
            {
                ResetSharedMemory(signalStop: true);
                _targetProcessId = null;
                return GameEventMonitorStatus.WaitingForMonitor;
            }

            if (_targetProcessId != targetProcessId)
            {
                ResetSharedMemory(signalStop: true);
                _targetProcessId = targetProcessId;
            }

            if (!TryEnsureSharedMemory(targetProcessId.Value))
            {
                return GameEventMonitorStatus.WaitingForMonitor;
            }

            if (!_readinessSignalObserved)
            {
                _readinessSignalObserved = _eventHandle?.WaitOne(0) == true;
                if (!_readinessSignalObserved)
                {
                    return GameEventMonitorStatus.WaitingForMonitor;
                }
            }
            else
            {
                _eventHandle?.WaitOne(0);
            }

            byte[] snapshot = new byte[SharedMemorySize];
            try
            {
                using MemoryMappedViewAccessor accessor = _sharedMemory!.CreateViewAccessor(
                    0,
                    SharedMemorySize,
                    MemoryMappedFileAccess.Read);
                if (!TryReadStableSnapshot(new MemoryMappedStableSnapshotReader(accessor), snapshot))
                {
                    return GameEventMonitorStatus.WaitingForMonitor;
                }
            }
            catch (IOException)
            {
                ResetSharedMemory();
                return GameEventMonitorStatus.WaitingForMonitor;
            }
            catch (UnauthorizedAccessException)
            {
                ResetSharedMemory();
                return GameEventMonitorStatus.WaitingForMonitor;
            }

            return DecodeSnapshot(snapshot, receivedAt);
        }

        public void RequestStop(int? targetProcessId)
        {
            if (targetProcessId is int processId and > 0)
            {
                SignalStopEvent(processId);
                ResetSharedMemory();
            }
            else
            {
                ResetSharedMemory(signalStop: true);
            }

            _targetProcessId = null;
        }

        public bool IsStopComplete(int targetProcessId)
        {
            if (targetProcessId <= 0)
            {
                return true;
            }

            try
            {
                using EventWaitHandle stopEventHandle = EventWaitHandle.OpenExisting(BuildStopEventHandleName(targetProcessId));
                return false;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public void Dispose()
        {
            ResetSharedMemory(signalStop: true);
        }

        internal static GameEventMonitorStatus DecodeSnapshot(ReadOnlySpan<byte> snapshot, DateTimeOffset receivedAt)
        {
            return DecodeSnapshot(snapshot, receivedAt, unchecked((uint)Environment.TickCount64));
        }

        internal static GameEventMonitorStatus DecodeSnapshot(
            ReadOnlySpan<byte> snapshot,
            DateTimeOffset receivedAt,
            uint receivedAtTick)
        {
            if (snapshot.Length < HeaderSize)
            {
                return new GameEventMonitorStatus(
                    GameCompatibilityState.UnsupportedVersion,
                    0,
                    0,
                    0,
                    []);
            }

            uint magic = ReadUInt32(snapshot, EventMonitorSnapshotContract.SharedSnapshotMagicOffset);
            uint version = ReadUInt32(snapshot, EventMonitorSnapshotContract.SharedSnapshotVersionOffset);
            if (magic != SnapshotMagic || !EventMonitorSnapshotContract.IsSupportedSnapshotVersion(version))
            {
                return new GameEventMonitorStatus(
                    GameCompatibilityState.UnsupportedVersion,
                    0,
                    0,
                    0,
                    []);
            }

            GameCompatibilityState compatibilityState = ReadCompatibilityState(
                snapshot.Slice(
                    EventMonitorSnapshotContract.SharedSnapshotCompatibilityStateOffset,
                    EventMonitorSnapshotContract.SharedSnapshotCompatibilityStateSize));
            uint eventWriteIndex = ReadUInt32(snapshot, EventMonitorSnapshotContract.SharedSnapshotEventWriteIndexOffset);
            uint droppedEventCount = ReadUInt32(snapshot, EventMonitorSnapshotContract.SharedSnapshotDroppedEventCountOffset);
            uint eventCount = ReadUInt32(snapshot, EventMonitorSnapshotContract.SharedSnapshotEventCountOffset);
            uint droppedNotifyCount = ReadUInt32(snapshot, EventMonitorSnapshotContract.SharedSnapshotDroppedNotifyCountOffset);
            uint publishedNotifyCount = ReadUInt32(snapshot, EventMonitorSnapshotContract.SharedSnapshotPublishedNotifyCountOffset);
            int readableEventCount = (int)Math.Min(eventCount, MaxEventCount);
            var events = new List<GameEvent>(readableEventCount);
            int startSlot = readableEventCount == MaxEventCount
                ? (int)(eventWriteIndex % MaxEventCount)
                : 0;

            for (int index = 0; index < readableEventCount; index++)
            {
                int slot = (startSlot + index) % MaxEventCount;
                int recordOffset = EventMonitorSnapshotContract.SharedSnapshotEventsOffset + (slot * EventRecordSize);
                if (recordOffset + EventRecordSize > snapshot.Length)
                {
                    break;
                }

                ReadOnlySpan<byte> record = snapshot.Slice(recordOffset, EventRecordSize);
                GameEventType eventType = ReadEventType(
                    record.Slice(
                        EventMonitorSnapshotContract.GameEventRecordEventTypeOffset,
                        EventMonitorSnapshotContract.GameEventRecordEventTypeSize));
                int levelTime = ReadInt32(record, EventMonitorSnapshotContract.GameEventRecordLevelTimeOffset);
                uint ownerId = ReadUInt32(record, EventMonitorSnapshotContract.GameEventRecordOwnerIdOffset);
                uint stringValue = ReadUInt32(record, EventMonitorSnapshotContract.GameEventRecordStringValueOffset);
                uint eventTick = ReadUInt32(record, EventMonitorSnapshotContract.GameEventRecordTickOffset);
                string eventName = ReadEventName(record[EventNameOffset..(EventNameOffset + MaxEventNameBytes)]);
                string weaponName = ReadEventName(record[WeaponNameOffset..(WeaponNameOffset + MaxWeaponNameBytes)]);
                if (string.IsNullOrWhiteSpace(eventName))
                {
                    continue;
                }

                if (eventType == GameEventType.Unknown)
                {
                    eventType = MapEventName(eventName);
                }

                DateTimeOffset eventReceivedAt = ConvertNativeTickToReceivedAt(receivedAt, receivedAtTick, eventTick);
                events.Add(new GameEvent(
                    eventType,
                    eventName,
                    levelTime,
                    ownerId,
                    stringValue,
                    eventReceivedAt,
                    string.IsNullOrWhiteSpace(weaponName) ? null : weaponName));
            }

            return new GameEventMonitorStatus(
                compatibilityState,
                droppedEventCount,
                droppedNotifyCount,
                publishedNotifyCount,
                events);
        }

        internal static GameEventType MapEventName(string eventName)
        {
            return eventName switch
            {
                "start_of_round" => GameEventType.StartOfRound,
                "end_of_round" => GameEventType.EndOfRound,
                "end_game" => GameEventType.EndGame,
                "round_changed" => GameEventType.RoundChanged,
                "points_changed" => GameEventType.PointsChanged,
                "kills_changed" => GameEventType.KillsChanged,
                "downs_changed" => GameEventType.DownsChanged,
                "vm_notify_candidate_rejected" => GameEventType.NotifyCandidateRejected,
                "vm_notify_entry_candidate" => GameEventType.NotifyEntryCandidate,
                "sl_convert_candidate" => GameEventType.StringResolverCandidate,
                "sl_get_string_of_size_candidate" => GameEventType.StringResolverCandidate,
                "vm_notify_observed" => GameEventType.NotifyObserved,
                "notify_log_opened" => GameEventType.NotifyObserved,
                "randomization_done"
                    or "user_grabbed_weapon"
                    or "chest_accessed"
                    or "box_moving"
                    or "weapon_fly_away_start"
                    or "weapon_fly_away_end"
                    or "arrived"
                    or "left"
                    or "closed" => GameEventType.BoxEvent,
                _ => GameEventType.Unknown
            };
        }

        internal static DateTimeOffset ConvertNativeTickToReceivedAt(
            DateTimeOffset snapshotReceivedAt,
            uint snapshotTick,
            uint eventTick)
        {
            uint elapsedMilliseconds = unchecked(snapshotTick - eventTick);
            return snapshotReceivedAt - TimeSpan.FromMilliseconds(elapsedMilliseconds);
        }

        private static GameCompatibilityState ReadCompatibilityState(ReadOnlySpan<byte> value)
        {
            int rawValue = BinaryPrimitives.ReadInt32LittleEndian(value);
            return Enum.IsDefined(typeof(GameCompatibilityState), rawValue)
                ? (GameCompatibilityState)rawValue
                : GameCompatibilityState.Unknown;
        }

        private static GameEventType ReadEventType(ReadOnlySpan<byte> value)
        {
            int rawValue = BinaryPrimitives.ReadInt32LittleEndian(value);
            return Enum.IsDefined(typeof(GameEventType), rawValue)
                ? (GameEventType)rawValue
                : GameEventType.Unknown;
        }

        private static int ReadInt32(ReadOnlySpan<byte> value, int offset)
        {
            return BinaryPrimitives.ReadInt32LittleEndian(value.Slice(offset, sizeof(int)));
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> value, int offset)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(value.Slice(offset, sizeof(uint)));
        }

        private static string ReadEventName(ReadOnlySpan<byte> value)
        {
            int terminatorIndex = value.IndexOf((byte)0);
            ReadOnlySpan<byte> nameBytes = terminatorIndex >= 0 ? value[..terminatorIndex] : value;
            return Encoding.UTF8.GetString(nameBytes);
        }

        internal static string BuildSharedMemoryName(int processId)
        {
            return SharedMemoryNamePrefix + processId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static string BuildEventHandleName(int processId)
        {
            return EventHandleNamePrefix + processId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static string BuildStopEventHandleName(int processId)
        {
            return StopEventHandleNamePrefix + processId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static bool TryReadStableSnapshot(IStableSnapshotReader reader, byte[] snapshot)
        {
            for (int attempt = 0; attempt < StableReadAttempts; attempt++)
            {
                reader.ReadWriteSequence(out uint beforeSequence);
                if ((beforeSequence & 1) != 0)
                {
                    Thread.Sleep(1);
                    continue;
                }

                reader.ReadSnapshot(snapshot);
                reader.ReadWriteSequence(out uint afterSequence);
                if (beforeSequence == afterSequence && (afterSequence & 1) == 0)
                {
                    return true;
                }

                Thread.Sleep(1);
            }

            return false;
        }

        private sealed class MemoryMappedStableSnapshotReader(MemoryMappedViewAccessor accessor) : IStableSnapshotReader
        {
            public void ReadWriteSequence(out uint sequence)
            {
                accessor.Read(WriteSequenceOffset, out sequence);
            }

            public void ReadSnapshot(byte[] snapshot)
            {
                accessor.ReadArray(0, snapshot, 0, snapshot.Length);
            }
        }

        private bool TryEnsureSharedMemory(int targetProcessId)
        {
            if (_sharedMemory is not null && _eventHandle is not null)
            {
                return true;
            }

            try
            {
                _eventHandle ??= EventWaitHandle.OpenExisting(BuildEventHandleName(targetProcessId));
                _stopEventHandle ??= EventWaitHandle.OpenExisting(BuildStopEventHandleName(targetProcessId));
                _sharedMemory ??= MemoryMappedFile.OpenExisting(
                    BuildSharedMemoryName(targetProcessId),
                    MemoryMappedFileRights.Read);
                return true;
            }
            catch (FileNotFoundException)
            {
                ResetSharedMemory();
                return false;
            }
            catch (IOException)
            {
                ResetSharedMemory();
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                ResetSharedMemory();
                return false;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                ResetSharedMemory();
                return false;
            }
        }

        private void SignalStopEvent(int targetProcessId)
        {
            try
            {
                if (_targetProcessId == targetProcessId && _stopEventHandle is not null)
                {
                    _stopEventHandle.Set();
                    return;
                }

                using EventWaitHandle stopEventHandle = EventWaitHandle.OpenExisting(BuildStopEventHandleName(targetProcessId));
                stopEventHandle.Set();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }

        private void ResetSharedMemory(bool signalStop = false)
        {
            if (signalStop)
            {
                try
                {
                    _stopEventHandle?.Set();
                }
                catch (ObjectDisposedException)
                {
                    _stopEventHandle = null;
                }
            }

            _sharedMemory?.Dispose();
            _sharedMemory = null;
            _eventHandle?.Dispose();
            _eventHandle = null;
            _stopEventHandle?.Dispose();
            _stopEventHandle = null;
            _readinessSignalObserved = false;
        }
    }
}
