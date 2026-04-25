namespace BO2.Services
{
    public sealed record PlayerStatAddressMap(
        uint PointsAddress,
        uint KillsAddress,
        uint DownsAddress,
        uint RevivesAddress,
        uint HeadshotsAddress)
    {
        public static PlayerStatAddressMap SteamZombies { get; } = new(
            0x0234C068U,
            0x0234C080U,
            0x0234C084U,
            0x0234C088U,
            0x0234C08CU);
    }
}
