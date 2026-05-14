using System;
using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace BO2.Services
{
    internal interface IGameMapIdentityReader : IDisposable
    {
        GameMapIdentityReadResult ReadMapIdentity(DetectedGame detectedGame);

        void ClearAttachedGame();
    }

    internal sealed record GameMapIdentityAddressMap
    {
        public required uint DvarBucketTableAddress { get; init; }

        public required int DvarBucketCount { get; init; }

        public static GameMapIdentityAddressMap SteamZombies { get; } = new()
        {
            DvarBucketTableAddress = 0x029F4548U,
            DvarBucketCount = 1024
        };

        public static GameMapIdentityAddressMap? ForVariant(GameVariant variant)
        {
            return variant == GameVariant.SteamZombies ? SteamZombies : null;
        }
    }

    internal sealed class GameMapIdentityReader : IGameMapIdentityReader
    {
        private const uint MinimumValidPointer = 0x00010000U;
        private const uint DvarNamePointerOffset = 0x00U;
        private const uint DvarCurrentValueOffset = 0x18U;
        private const uint DvarNextPointerOffset = 0x58U;
        private const int PointerSize = sizeof(int);
        private const int MaxDvarStringBytes = 128;
        private const int MaxDvarChainLength = 256;

        private readonly IProcessMemoryAccessor _processMemoryAccessor;

        public GameMapIdentityReader()
            : this(new WindowsProcessMemoryAccessor())
        {
        }

        internal GameMapIdentityReader(IProcessMemoryAccessor processMemoryAccessor)
        {
            ArgumentNullException.ThrowIfNull(processMemoryAccessor);

            _processMemoryAccessor = processMemoryAccessor;
        }

        public GameMapIdentityReadResult ReadMapIdentity(DetectedGame detectedGame)
        {
            ArgumentNullException.ThrowIfNull(detectedGame);

            GameMapIdentityAddressMap? addressMap = GameMapIdentityAddressMap.ForVariant(detectedGame.Variant);
            if (addressMap is null)
            {
                ClearAttachedGame();
                return GameMapIdentityReadResult.UnsupportedVariant(detectedGame);
            }

            try
            {
                _processMemoryAccessor.Attach(detectedGame.ProcessId, detectedGame.ProcessName);

                DvarStringReadResult baseMap = ReadDvarString(addressMap, "mapname");
                if (baseMap.Status != DvarStringReadStatus.Found)
                {
                    return ToMapIdentityReadResult(detectedGame, baseMap.Status);
                }

                if (!string.Equals(baseMap.Value?.Trim(), "zm_transit", StringComparison.OrdinalIgnoreCase))
                {
                    return GameMapIdentityResolver.ResolveSupportedMap(
                        detectedGame,
                        baseMap.Value,
                        null);
                }

                DvarStringReadResult startLocation = ReadDvarString(addressMap, "ui_zm_mapstartlocation");
                if (startLocation.Status != DvarStringReadStatus.Found)
                {
                    return ToMapIdentityReadResult(detectedGame, startLocation.Status);
                }

                return GameMapIdentityResolver.ResolveSupportedMap(
                    detectedGame,
                    baseMap.Value,
                    startLocation.Value);
            }
            catch (Exception ex) when (IsReadFailure(ex))
            {
                ClearAttachedGame();
                return GameMapIdentityReadResult.Unreadable(detectedGame);
            }
        }

        public void ClearAttachedGame()
        {
            _processMemoryAccessor.Close();
        }

        public void Dispose()
        {
            _processMemoryAccessor.Dispose();
        }

        private DvarStringReadResult ReadDvarString(
            GameMapIdentityAddressMap addressMap,
            string dvarName)
        {
            if (addressMap.DvarBucketCount <= 0)
            {
                return DvarStringReadResult.Malformed;
            }

            int bucket = GetBucketIndex(dvarName, addressMap.DvarBucketCount);
            uint bucketAddress = addressMap.DvarBucketTableAddress + ((uint)bucket * PointerSize);
            uint dvarPointer = ReadPointer(bucketAddress, $"dvar bucket {bucket.ToString(CultureInfo.InvariantCulture)}");
            int chainLength = 0;
            while (dvarPointer != 0)
            {
                if (!IsValidPointer(dvarPointer) || chainLength++ >= MaxDvarChainLength)
                {
                    return DvarStringReadResult.Malformed;
                }

                StringReadResult nameRead = ReadStringPointer(
                    dvarPointer + DvarNamePointerOffset,
                    "dvar name");
                if (nameRead.Status != DvarStringReadStatus.Found)
                {
                    return DvarStringReadResult.FromStatus(nameRead.Status);
                }

                if (string.Equals(nameRead.Value, dvarName, StringComparison.OrdinalIgnoreCase))
                {
                    StringReadResult valueRead = ReadStringPointer(
                        dvarPointer + DvarCurrentValueOffset,
                        dvarName);
                    return valueRead is { Status: DvarStringReadStatus.Found, Value: string value }
                        ? DvarStringReadResult.Found(value)
                        : DvarStringReadResult.FromStatus(valueRead.Status);
                }

                dvarPointer = ReadPointer(dvarPointer + DvarNextPointerOffset, "next dvar");
            }

            return DvarStringReadResult.Missing;
        }

        private static int GetBucketIndex(string dvarName, int bucketCount)
        {
            uint hash = 5381U;
            foreach (char character in dvarName)
            {
                char lower = char.ToLowerInvariant(character);
                hash = unchecked(((hash << 5) + hash) + lower);
            }

            return (int)(hash & (uint)(bucketCount - 1));
        }

        private StringReadResult ReadStringPointer(uint pointerAddress, string valueName)
        {
            uint stringPointer = ReadPointer(pointerAddress, valueName + " pointer");
            if (!IsValidPointer(stringPointer))
            {
                return StringReadResult.Malformed;
            }

            return ReadNullTerminatedAsciiString(stringPointer, valueName);
        }

        private StringReadResult ReadNullTerminatedAsciiString(uint address, string valueName)
        {
            byte[] buffer = new byte[MaxDvarStringBytes];
            for (int i = 0; i < buffer.Length; i++)
            {
                byte value = _processMemoryAccessor.ReadByte(address + (uint)i, valueName);
                if (value == 0)
                {
                    if (i == 0)
                    {
                        return StringReadResult.Missing;
                    }

                    return StringReadResult.Found(Encoding.ASCII.GetString(buffer, 0, i));
                }

                if (value < 0x20 || value > 0x7E)
                {
                    return StringReadResult.Malformed;
                }

                buffer[i] = value;
            }

            return StringReadResult.Malformed;
        }

        private uint ReadPointer(uint address, string valueName)
        {
            return unchecked((uint)_processMemoryAccessor.ReadInt32(address, valueName));
        }

        private static bool IsValidPointer(uint pointer)
        {
            return pointer >= MinimumValidPointer;
        }

        private static GameMapIdentityReadResult ToMapIdentityReadResult(
            DetectedGame detectedGame,
            DvarStringReadStatus status)
        {
            return status switch
            {
                DvarStringReadStatus.Missing => GameMapIdentityReadResult.MissingMapIdentity(detectedGame),
                DvarStringReadStatus.Unreadable => GameMapIdentityReadResult.Unreadable(detectedGame),
                DvarStringReadStatus.Malformed => GameMapIdentityReadResult.Malformed(detectedGame),
                _ => GameMapIdentityReadResult.MissingMapIdentity(detectedGame)
            };
        }

        private static bool IsReadFailure(Exception exception)
        {
            return exception is ArgumentException or InvalidOperationException or Win32Exception;
        }

        private readonly record struct DvarStringReadResult(
            DvarStringReadStatus Status,
            string? Value)
        {
            public static DvarStringReadResult Missing { get; } = new(
                DvarStringReadStatus.Missing,
                null);

            public static DvarStringReadResult Malformed { get; } = new(
                DvarStringReadStatus.Malformed,
                null);

            public static DvarStringReadResult Found(string value)
            {
                return new DvarStringReadResult(DvarStringReadStatus.Found, value);
            }

            public static DvarStringReadResult FromStatus(DvarStringReadStatus status)
            {
                return new DvarStringReadResult(status, null);
            }
        }

        private readonly record struct StringReadResult(
            DvarStringReadStatus Status,
            string? Value)
        {
            public static StringReadResult Missing { get; } = new(
                DvarStringReadStatus.Missing,
                null);

            public static StringReadResult Malformed { get; } = new(
                DvarStringReadStatus.Malformed,
                null);

            public static StringReadResult Found(string? value)
            {
                return value is null
                    ? Missing
                    : new StringReadResult(DvarStringReadStatus.Found, value);
            }
        }

        private enum DvarStringReadStatus
        {
            Found,
            Missing,
            Unreadable,
            Malformed
        }
    }
}
