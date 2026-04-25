namespace BO2.Services
{
    public sealed record PlayerStats(
        int Points,
        int Kills,
        int Downs,
        int Revives,
        int Headshots);
}
