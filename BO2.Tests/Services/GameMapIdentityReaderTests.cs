using System;
using System.ComponentModel;
using BO2.Services;
using BO2.Tests.Fakes;
using Xunit;

namespace BO2.Tests.Services
{
    public sealed class GameMapIdentityReaderTests
    {
        private const uint BucketTableAddress = 0x029F4548U;
        private const int PointerSize = sizeof(int);

        [Fact]
        public void ReadMapIdentity_WhenTownDvarsArePresent_ReturnsSupportedTown()
        {
            DetectedGame detectedGame = CreateSteamZombiesGame();
            FakeProcessMemoryAccessor memory = new();
            WriteDvarString(memory, "mapname", 0x02A32AA8U, 0x03000000U, "zm_transit");
            WriteDvarString(memory, "ui_zm_mapstartlocation", 0x02A0A308U, 0x03000100U, "town");
            var reader = new GameMapIdentityReader(memory);

            GameMapIdentityReadResult result = reader.ReadMapIdentity(detectedGame);

            Assert.Equal(GameMapIdentityReadStatus.SupportedMap, result.Status);
            Assert.True(result.IsSupportedMap);
            Assert.NotNull(result.Identity);
            Assert.Equal("zm_transit", result.Identity!.BaseMapToken);
            Assert.Equal("town", result.Identity.StartLocationToken);
            Assert.Equal("zm_transit_gump_town", result.Identity.InternalMapToken);
            Assert.Equal("Town", result.Identity.DisplayName);
            Assert.Equal(1, memory.AttachCallCount);
        }

        [Fact]
        public void ReadMapIdentity_WhenStartLocationIsFarm_ReturnsSupportedFarm()
        {
            DetectedGame detectedGame = CreateSteamZombiesGame();
            FakeProcessMemoryAccessor memory = new();
            WriteDvarString(memory, "mapname", 0x02A32AA8U, 0x03000000U, " ZM_TRANSIT ");
            WriteDvarString(memory, "ui_zm_mapstartlocation", 0x02A0A308U, 0x03000100U, " Farm ");
            var reader = new GameMapIdentityReader(memory);

            GameMapIdentityReadResult result = reader.ReadMapIdentity(detectedGame);

            Assert.Equal(GameMapIdentityReadStatus.SupportedMap, result.Status);
            Assert.True(result.IsSupportedMap);
            Assert.NotNull(result.Identity);
            Assert.Equal("zm_transit", result.Identity!.BaseMapToken);
            Assert.Equal("farm", result.Identity.StartLocationToken);
            Assert.Equal("zm_transit_gump_farm", result.Identity.InternalMapToken);
            Assert.Equal("Farm", result.Identity.DisplayName);
        }

        [Theory]
        [InlineData("zclassic", "zm_transit_gump_transit_zclassic", "TranZit")]
        [InlineData(" ZSTANDARD ", "zm_transit_gump_transit_zstandard", "Bus Depot")]
        public void ReadMapIdentity_WhenTransitStartLocationHasValidatedMode_ReturnsSupportedMap(
            string modeToken,
            string expectedInternalMapToken,
            string expectedDisplayName)
        {
            DetectedGame detectedGame = CreateSteamZombiesGame();
            FakeProcessMemoryAccessor memory = new();
            WriteDvarString(memory, "mapname", 0x02A32AA8U, 0x03000000U, "zm_transit");
            WriteDvarString(memory, "ui_zm_mapstartlocation", 0x02A0A308U, 0x03000100U, "transit");
            WriteDvarString(memory, "ui_gametype", 0x02A0A588U, 0x03000200U, modeToken);
            var reader = new GameMapIdentityReader(memory);

            GameMapIdentityReadResult result = reader.ReadMapIdentity(detectedGame);

            Assert.Equal(GameMapIdentityReadStatus.SupportedMap, result.Status);
            Assert.True(result.IsSupportedMap);
            Assert.NotNull(result.Identity);
            Assert.Equal("zm_transit", result.Identity!.BaseMapToken);
            Assert.Equal("transit", result.Identity.StartLocationToken);
            Assert.Equal(expectedInternalMapToken, result.Identity.InternalMapToken);
            Assert.Equal(expectedDisplayName, result.Identity.DisplayName);
        }

        [Theory]
        [InlineData("bus_depot")]
        [InlineData("busstation")]
        [InlineData("diner")]
        public void ReadMapIdentity_WhenGreenRunStartLocationIsUnsupported_ReturnsUnsupportedMapIdentity(
            string startLocationToken)
        {
            DetectedGame detectedGame = CreateSteamZombiesGame();
            FakeProcessMemoryAccessor memory = new();
            WriteDvarString(memory, "mapname", 0x02A32AA8U, 0x03000000U, "zm_transit");
            WriteDvarString(memory, "ui_zm_mapstartlocation", 0x02A0A308U, 0x03000100U, startLocationToken);
            var reader = new GameMapIdentityReader(memory);

            GameMapIdentityReadResult result = reader.ReadMapIdentity(detectedGame);

            Assert.Equal(GameMapIdentityReadStatus.UnsupportedMapIdentity, result.Status);
            Assert.False(result.IsSupportedMap);
            Assert.Null(result.Identity);
        }

        [Fact]
        public void ReadMapIdentity_WhenTransitModeIsUnsupported_ReturnsUnsupportedMapIdentity()
        {
            DetectedGame detectedGame = CreateSteamZombiesGame();
            FakeProcessMemoryAccessor memory = new();
            WriteDvarString(memory, "mapname", 0x02A32AA8U, 0x03000000U, "zm_transit");
            WriteDvarString(memory, "ui_zm_mapstartlocation", 0x02A0A308U, 0x03000100U, "transit");
            WriteDvarString(memory, "ui_gametype", 0x02A0A588U, 0x03000200U, "zencounter");
            var reader = new GameMapIdentityReader(memory);

            GameMapIdentityReadResult result = reader.ReadMapIdentity(detectedGame);

            Assert.Equal(GameMapIdentityReadStatus.UnsupportedMapIdentity, result.Status);
            Assert.False(result.IsSupportedMap);
            Assert.Null(result.Identity);
        }

        [Fact]
        public void ReadMapIdentity_WhenTransitModeDvarIsMissing_ReturnsMissingMapIdentity()
        {
            DetectedGame detectedGame = CreateSteamZombiesGame();
            FakeProcessMemoryAccessor memory = new();
            WriteDvarString(memory, "mapname", 0x02A32AA8U, 0x03000000U, "zm_transit");
            WriteDvarString(memory, "ui_zm_mapstartlocation", 0x02A0A308U, 0x03000100U, "transit");
            var reader = new GameMapIdentityReader(memory);

            GameMapIdentityReadResult result = reader.ReadMapIdentity(detectedGame);

            Assert.Equal(GameMapIdentityReadStatus.MissingMapIdentity, result.Status);
            Assert.False(result.IsSupportedMap);
            Assert.Null(result.Identity);
        }

        [Fact]
        public void ReadMapIdentity_WhenDvarIsMissing_ReturnsMissingMapIdentity()
        {
            DetectedGame detectedGame = CreateSteamZombiesGame();
            var reader = new GameMapIdentityReader(new FakeProcessMemoryAccessor());

            GameMapIdentityReadResult result = reader.ReadMapIdentity(detectedGame);

            Assert.Equal(GameMapIdentityReadStatus.MissingMapIdentity, result.Status);
            Assert.Null(result.Identity);
        }

        [Fact]
        public void ReadMapIdentity_WhenDvarStringIsMalformed_ReturnsMalformed()
        {
            DetectedGame detectedGame = CreateSteamZombiesGame();
            FakeProcessMemoryAccessor memory = new();
            uint dvarAddress = 0x02A32AA8U;
            uint nameAddress = 0x03000000U;
            uint valueAddress = 0x03000100U;
            memory.SetInt32(BucketAddress("mapname"), unchecked((int)dvarAddress));
            memory.SetInt32(dvarAddress, unchecked((int)nameAddress));
            memory.SetInt32(dvarAddress + 0x18U, unchecked((int)valueAddress));
            memory.SetInt32(dvarAddress + 0x58U, 0);
            WriteAscii(memory, nameAddress, "mapname");
            memory.SetByte(valueAddress, 0x01);
            var reader = new GameMapIdentityReader(memory);

            GameMapIdentityReadResult result = reader.ReadMapIdentity(detectedGame);

            Assert.Equal(GameMapIdentityReadStatus.Malformed, result.Status);
            Assert.Null(result.Identity);
        }

        [Fact]
        public void ReadMapIdentity_WhenMemoryReadFails_ReturnsUnreadableAndClearsAttach()
        {
            DetectedGame detectedGame = CreateSteamZombiesGame();
            FakeProcessMemoryAccessor memory = new();
            memory.SetInt32Exception(
                BucketAddress("mapname"),
                new Win32Exception(5, "denied"));
            var reader = new GameMapIdentityReader(memory);

            GameMapIdentityReadResult result = reader.ReadMapIdentity(detectedGame);

            Assert.Equal(GameMapIdentityReadStatus.Unreadable, result.Status);
            Assert.Equal(1, memory.CloseCallCount);
        }

        [Fact]
        public void ReadMapIdentity_WhenVariantUnsupported_ReturnsUnsupportedWithoutAttach()
        {
            DetectedGame detectedGame = new(
                GameVariant.RedactedZombies,
                "Redacted Zombies",
                "t6zm",
                2001,
                null,
                "Unsupported variant");
            FakeProcessMemoryAccessor memory = new();
            var reader = new GameMapIdentityReader(memory);

            GameMapIdentityReadResult result = reader.ReadMapIdentity(detectedGame);

            Assert.Equal(GameMapIdentityReadStatus.UnsupportedVariant, result.Status);
            Assert.Equal(0, memory.AttachCallCount);
            Assert.Equal(1, memory.CloseCallCount);
        }

        private static void WriteDvarString(
            FakeProcessMemoryAccessor memory,
            string name,
            uint dvarAddress,
            uint stringBaseAddress,
            string value)
        {
            uint nameAddress = stringBaseAddress;
            uint valueAddress = stringBaseAddress + 0x80U;
            memory.SetInt32(BucketAddress(name), unchecked((int)dvarAddress));
            memory.SetInt32(dvarAddress, unchecked((int)nameAddress));
            memory.SetInt32(dvarAddress + 0x18U, unchecked((int)valueAddress));
            memory.SetInt32(dvarAddress + 0x58U, 0);
            WriteAscii(memory, nameAddress, name);
            WriteAscii(memory, valueAddress, value);
        }

        private static void WriteAscii(FakeProcessMemoryAccessor memory, uint address, string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                memory.SetByte(address + (uint)i, (byte)value[i]);
            }

            memory.SetByte(address + (uint)value.Length, 0);
        }

        private static uint BucketAddress(string dvarName)
        {
            uint hash = ComputeDjb2Hash(dvarName);
            return BucketTableAddress + ((hash & 0x3ffU) * PointerSize);
        }

        private static uint ComputeDjb2Hash(string value)
        {
            uint hash = 5381U;
            foreach (char character in value)
            {
                char lower = char.ToLowerInvariant(character);
                hash = unchecked(((hash << 5) + hash) + lower);
            }

            return hash;
        }

        private static DetectedGame CreateSteamZombiesGame()
        {
            return new DetectedGame(
                GameVariant.SteamZombies,
                "Steam Zombies",
                "t6zm",
                1001,
                PlayerStatAddressMap.SteamZombies,
                null);
        }
    }
}
