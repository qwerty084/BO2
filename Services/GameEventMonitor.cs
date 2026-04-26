using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;

namespace BO2.Services
{
    public sealed class GameEventMonitor : IGameEventMonitor
    {
        public const string SharedMemoryName = "BO2MonitorSharedMem";
        public const string EventHandleName = "BO2MonitorEvent";

        internal const uint SnapshotMagic = 0x45324F42; // BO2E
        internal const uint SnapshotVersion = 1;
        internal const int MaxEventCount = 128;
        internal const int MaxEventNameBytes = 64;
        internal const int HeaderSize = 24;
        internal const int EventRecordSize = 72;
        internal const int SharedMemorySize = HeaderSize + (MaxEventCount * EventRecordSize);

        private MemoryMappedFile? _sharedMemory;

        public GameEventMonitorStatus ReadStatus(DateTimeOffset receivedAt)
        {
            if (!TryEnsureSharedMemory())
            {
                return GameEventMonitorStatus.WaitingForMonitor;
            }

            byte[] snapshot = new byte[SharedMemorySize];
            try
            {
                using MemoryMappedViewAccessor accessor = _sharedMemory!.CreateViewAccessor(
                    0,
                    SharedMemorySize,
                    MemoryMappedFileAccess.Read);
                accessor.ReadArray(0, snapshot, 0, snapshot.Length);
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

        public void Dispose()
        {
            ResetSharedMemory();
        }

        internal static GameEventMonitorStatus DecodeSnapshot(ReadOnlySpan<byte> snapshot, DateTimeOffset receivedAt)
        {
            if (snapshot.Length < HeaderSize)
            {
                return new GameEventMonitorStatus(
                    GameCompatibilityState.UnsupportedVersion,
                    0,
                    Array.Empty<GameEvent>());
            }

            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(snapshot[0..4]);
            uint version = BinaryPrimitives.ReadUInt32LittleEndian(snapshot[4..8]);
            if (magic != SnapshotMagic || version != SnapshotVersion)
            {
                return new GameEventMonitorStatus(
                    GameCompatibilityState.UnsupportedVersion,
                    0,
                    Array.Empty<GameEvent>());
            }

            GameCompatibilityState compatibilityState = ReadCompatibilityState(snapshot[8..12]);
            uint droppedEventCount = BinaryPrimitives.ReadUInt32LittleEndian(snapshot[16..20]);
            uint eventCount = BinaryPrimitives.ReadUInt32LittleEndian(snapshot[20..24]);
            int readableEventCount = (int)Math.Min(eventCount, MaxEventCount);
            var events = new List<GameEvent>(readableEventCount);

            for (int index = 0; index < readableEventCount; index++)
            {
                int recordOffset = HeaderSize + (index * EventRecordSize);
                if (recordOffset + EventRecordSize > snapshot.Length)
                {
                    break;
                }

                ReadOnlySpan<byte> record = snapshot.Slice(recordOffset, EventRecordSize);
                GameEventType eventType = ReadEventType(record[0..4]);
                int levelTime = BinaryPrimitives.ReadInt32LittleEndian(record[4..8]);
                string eventName = ReadEventName(record[8..(8 + MaxEventNameBytes)]);
                if (string.IsNullOrWhiteSpace(eventName))
                {
                    continue;
                }

                if (eventType == GameEventType.Unknown)
                {
                    eventType = MapEventName(eventName);
                }

                events.Add(new GameEvent(eventType, eventName, levelTime, receivedAt));
            }

            return new GameEventMonitorStatus(compatibilityState, droppedEventCount, events);
        }

        internal static GameEventType MapEventName(string eventName)
        {
            return eventName switch
            {
                "start_of_round" => GameEventType.StartOfRound,
                "end_of_round" => GameEventType.EndOfRound,
                "powerup_grabbed" => GameEventType.PowerUpGrabbed,
                "dog_round_starting" => GameEventType.DogRoundStarting,
                "power_on" => GameEventType.PowerOn,
                "end_game" => GameEventType.EndGame,
                "perk_bought" => GameEventType.PerkBought,
                "round_changed" => GameEventType.RoundChanged,
                "points_changed" => GameEventType.PointsChanged,
                "kills_changed" => GameEventType.KillsChanged,
                "downs_changed" => GameEventType.DownsChanged,
                "vm_notify_candidate_rejected" => GameEventType.NotifyCandidateRejected,
                "vm_notify_entry_candidate" => GameEventType.NotifyEntryCandidate,
                "sl_convert_candidate" => GameEventType.StringResolverCandidate,
                "vm_notify_observed" => GameEventType.NotifyObserved,
                _ => GameEventType.Unknown
            };
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

        private static string ReadEventName(ReadOnlySpan<byte> value)
        {
            int terminatorIndex = value.IndexOf((byte)0);
            ReadOnlySpan<byte> nameBytes = terminatorIndex >= 0 ? value[..terminatorIndex] : value;
            return Encoding.UTF8.GetString(nameBytes);
        }

        private bool TryEnsureSharedMemory()
        {
            if (_sharedMemory is not null)
            {
                return true;
            }

            try
            {
                _sharedMemory = MemoryMappedFile.OpenExisting(SharedMemoryName, MemoryMappedFileRights.Read);
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
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

        private void ResetSharedMemory()
        {
            _sharedMemory?.Dispose();
            _sharedMemory = null;
        }
    }
}
