namespace BO2.Services
{
    public sealed record PlayerStatAddressMap
    {
        public required ScoreStatAddresses Scores { get; init; }

        public required DerivedPlayerStateAddresses DerivedPlayerState { get; init; }

        public required PlayerCandidateAddresses Candidates { get; init; }

        public static PlayerStatAddressMap SteamZombies { get; } = new()
        {
            Scores = new()
            {
                PointsAddress = 0x0234C068U,
                KillsAddress = 0x0234C080U,
                DownsAddress = 0x0234C084U,
                RevivesAddress = 0x0234C088U,
                HeadshotsAddress = 0x0234C08CU
            },
            DerivedPlayerState = new()
            {
                LocalPlayerBaseAddress = 0x02346AA0U,
                PositionXAddress = 0x02346AC8U,
                PositionYAddress = 0x02346ACCU,
                PositionZAddress = 0x02346AD0U
            },
            Candidates = new()
            {
                LegacyHealthAddress = 0x02346C48U,
                PlayerInfoHealthAddress = 0x02346CD8U,
                GEntityPlayerHealthAddress = 0x021C5868U,
                VelocityXAddress = 0x02346AD4U,
                VelocityYAddress = 0x02346AD8U,
                VelocityZAddress = 0x02346ADCU,
                GravityAddress = 0x02346B2CU,
                SpeedAddress = 0x02346B34U,
                LastJumpHeightAddress = 0x02346B64U,
                AdsAmountAddress = 0x02346C84U,
                ViewAngleXAddress = 0x02346C9CU,
                ViewAngleYAddress = 0x02346CA0U,
                HeightIntAddress = 0x02346CA4U,
                HeightFloatAddress = 0x02346CA8U,
                AmmoSlot0Address = 0x02346EC8U,
                AmmoSlot1Address = 0x02346ECCU,
                LethalAmmoAddress = 0x02346ED0U,
                AmmoSlot2Address = 0x02346ED4U,
                TacticalAmmoAddress = 0x02346ED8U,
                AmmoSlot3Address = 0x02346EDCU,
                AmmoSlot4Address = 0x02346EE0U,
                AlternateKillsAddress = 0x0234C06CU,
                AlternateHeadshotsAddress = 0x0234C098U,
                SecondaryKillsAddress = 0x0234C0C0U,
                SecondaryHeadshotsAddress = 0x0234C0FCU,
                RoundAddress = 0x0233FA10U,
                GEntityArrayAddress = 0x021C56C0U,
                Zombie0GEntityAddress = 0x021C9B28U,
                GEntitySize = 0x0000031CU
            }
        };
    }

    public sealed record ScoreStatAddresses
    {
        public required uint PointsAddress { get; init; }

        public required uint KillsAddress { get; init; }

        public required uint DownsAddress { get; init; }

        public required uint RevivesAddress { get; init; }

        public required uint HeadshotsAddress { get; init; }
    }

    public sealed record DerivedPlayerStateAddresses
    {
        public required uint LocalPlayerBaseAddress { get; init; }

        public required uint PositionXAddress { get; init; }

        public required uint PositionYAddress { get; init; }

        public required uint PositionZAddress { get; init; }
    }

    public sealed record PlayerCandidateAddresses
    {
        public required uint LegacyHealthAddress { get; init; }

        public required uint PlayerInfoHealthAddress { get; init; }

        public required uint GEntityPlayerHealthAddress { get; init; }

        public required uint VelocityXAddress { get; init; }

        public required uint VelocityYAddress { get; init; }

        public required uint VelocityZAddress { get; init; }

        public required uint GravityAddress { get; init; }

        public required uint SpeedAddress { get; init; }

        public required uint LastJumpHeightAddress { get; init; }

        public required uint AdsAmountAddress { get; init; }

        public required uint ViewAngleXAddress { get; init; }

        public required uint ViewAngleYAddress { get; init; }

        public required uint HeightIntAddress { get; init; }

        public required uint HeightFloatAddress { get; init; }

        public required uint AmmoSlot0Address { get; init; }

        public required uint AmmoSlot1Address { get; init; }

        public required uint LethalAmmoAddress { get; init; }

        public required uint AmmoSlot2Address { get; init; }

        public required uint TacticalAmmoAddress { get; init; }

        public required uint AmmoSlot3Address { get; init; }

        public required uint AmmoSlot4Address { get; init; }

        public required uint AlternateKillsAddress { get; init; }

        public required uint AlternateHeadshotsAddress { get; init; }

        public required uint SecondaryKillsAddress { get; init; }

        public required uint SecondaryHeadshotsAddress { get; init; }

        public required uint RoundAddress { get; init; }

        public required uint GEntityArrayAddress { get; init; }

        public required uint Zombie0GEntityAddress { get; init; }

        public required uint GEntitySize { get; init; }
    }
}
