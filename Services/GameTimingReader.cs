using System;
using System.ComponentModel;

namespace BO2.Services
{
    internal interface IGameTimingReader : IDisposable
    {
        GameTimingReadResult ReadGameTiming(DetectedGame detectedGame);

        void ClearAttachedGame();
    }

    internal sealed class GameTimingReader : IGameTimingReader
    {
        private const uint MinimumValidPointer = 0x00010000U;

        private readonly IProcessMemoryAccessor _processMemoryAccessor;

        public GameTimingReader()
            : this(new WindowsProcessMemoryAccessor())
        {
        }

        internal GameTimingReader(IProcessMemoryAccessor processMemoryAccessor)
        {
            ArgumentNullException.ThrowIfNull(processMemoryAccessor);

            _processMemoryAccessor = processMemoryAccessor;
        }

        public GameTimingReadResult ReadGameTiming(DetectedGame detectedGame)
        {
            ArgumentNullException.ThrowIfNull(detectedGame);

            GameTimingAddressMap? addressMap = GameTimingAddressMap.ForVariant(detectedGame.Variant);
            if (addressMap is null)
            {
                return GameTimingReadResult.UnsupportedTiming(detectedGame);
            }

            try
            {
                _processMemoryAccessor.Attach(detectedGame.ProcessId, detectedGame.ProcessName);

                if (!TryReadBoolean(addressMap.ServerRunningAddress, "sv_running", out bool serverRunning))
                {
                    return GameTimingReadResult.InvalidTimingSourceState(detectedGame);
                }

                if (!serverRunning)
                {
                    return GameTimingReadResult.InactiveLobbyState(detectedGame);
                }

                if (!TryReadBoolean(addressMap.ClientPausedAddress, "cl_paused", out bool clientPaused))
                {
                    return GameTimingReadResult.InvalidTimingSourceState(detectedGame);
                }

                uint clientActivePointer = unchecked((uint)ReadInt32(
                    addressMap.ClientActivePointerAddress,
                    "client active pointer"));
                if (!IsValidPointer(clientActivePointer))
                {
                    return GameTimingReadResult.InvalidTimingSourceState(detectedGame);
                }

                if (!TryAddOffset(
                    clientActivePointer,
                    addressMap.SnapshotValidOffset,
                    out uint snapshotValidAddress)
                    || !TryAddOffset(
                        clientActivePointer,
                        addressMap.GameTimeMillisecondsOffset,
                        out uint gameTimeMillisecondsAddress))
                {
                    return GameTimingReadResult.InvalidTimingSourceState(detectedGame);
                }

                int snapshotValid = ReadInt32(snapshotValidAddress, "client snapshot valid");
                if (snapshotValid != 1)
                {
                    return GameTimingReadResult.InvalidTimingSourceState(detectedGame);
                }

                int gameTimeMilliseconds = ReadInt32(gameTimeMillisecondsAddress, "game time milliseconds");
                if (gameTimeMilliseconds <= 0)
                {
                    return GameTimingReadResult.InvalidTimingSourceState(detectedGame);
                }

                return GameTimingReadResult.SupportedTiming(
                    detectedGame,
                    TimeSpan.FromMilliseconds(gameTimeMilliseconds),
                    clientPaused);
            }
            catch (Exception ex) when (IsReadFailure(ex))
            {
                HandleReadFailure();
                return GameTimingReadResult.GenericReadFailure(detectedGame);
            }
        }

        public void Dispose()
        {
            _processMemoryAccessor.Dispose();
        }

        internal void ClearAttachedGame()
        {
            _processMemoryAccessor.Close();
        }

        void IGameTimingReader.ClearAttachedGame()
        {
            ClearAttachedGame();
        }

        private bool TryReadBoolean(uint address, string valueName, out bool value)
        {
            int rawValue = ReadInt32(address, valueName);
            if (rawValue == 0)
            {
                value = false;
                return true;
            }

            if (rawValue == 1)
            {
                value = true;
                return true;
            }

            value = false;
            return false;
        }

        private int ReadInt32(uint address, string valueName)
        {
            return _processMemoryAccessor.ReadInt32(address, valueName);
        }

        private static bool IsValidPointer(uint pointer)
        {
            return pointer >= MinimumValidPointer;
        }

        private static bool TryAddOffset(uint pointer, uint offset, out uint address)
        {
            if (pointer > uint.MaxValue - offset)
            {
                address = 0;
                return false;
            }

            address = pointer + offset;
            return true;
        }

        private static bool IsReadFailure(Exception exception)
        {
            return exception is ArgumentException or InvalidOperationException or Win32Exception;
        }

        private void HandleReadFailure()
        {
            ClearAttachedGame();
        }
    }
}
