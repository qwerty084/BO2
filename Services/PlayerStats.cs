namespace BO2.Services
{
    public sealed record PlayerStats(
        int Points,
        int Kills,
        int Downs,
        int Revives,
        int Headshots,
        PlayerCandidateStats Candidates);

    public sealed record PlayerCandidateStats(
        float? PositionX,
        float? PositionY,
        float? PositionZ,
        int? LegacyHealth,
        int? PlayerInfoHealth,
        int? GEntityPlayerHealth,
        float? VelocityX,
        float? VelocityY,
        float? VelocityZ,
        int? Gravity,
        int? Speed,
        float? LastJumpHeight,
        float? AdsAmount,
        float? ViewAngleX,
        float? ViewAngleY,
        int? HeightInt,
        float? HeightFloat,
        int? AmmoSlot0,
        int? AmmoSlot1,
        int? LethalAmmo,
        int? AmmoSlot2,
        int? TacticalAmmo,
        int? AmmoSlot3,
        int? AmmoSlot4,
        int? AlternateKills,
        int? AlternateHeadshots,
        int? SecondaryKills,
        int? SecondaryHeadshots,
        int? Round);
}
