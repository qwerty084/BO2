namespace BO2.Services
{
    public sealed record PlayerStatAddressMap(
        int PointsAddress,
        int KillsAddress,
        int DownsAddress,
        int RevivesAddress,
        int HeadshotsAddress)
    {
        public static PlayerStatAddressMap SteamZombies { get; } = new(
            0x0234C068,
            0x0234C080,
            0x0234C084,
            0x0234C088,
            0x0234C08C);
    }
}
