namespace BO2.Services
{
    internal sealed record GameTimingAddressMap
    {
        public required uint ServerRunningAddress { get; init; }

        public required uint ClientPausedAddress { get; init; }

        public required uint ClientActivePointerAddress { get; init; }

        public required uint SnapshotValidOffset { get; init; }

        public required uint GameTimeMillisecondsOffset { get; init; }

        public static GameTimingAddressMap SteamZombies { get; } = new()
        {
            ServerRunningAddress = 0x02A09F00U,
            ClientPausedAddress = 0x02A09DE0U,
            ClientActivePointerAddress = 0x0119DC04U,
            SnapshotValidOffset = 0x00000050U,
            GameTimeMillisecondsOffset = 0x00000058U
        };

        public static GameTimingAddressMap? ForVariant(GameVariant variant)
        {
            return variant == GameVariant.SteamZombies ? SteamZombies : null;
        }
    }
}
