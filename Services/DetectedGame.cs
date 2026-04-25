namespace BO2.Services
{
    public sealed record DetectedGame(
        GameVariant Variant,
        string DisplayName,
        string ProcessName,
        int ProcessId,
        PlayerStatAddressMap? AddressMap,
        string? UnsupportedReason)
    {
        public bool IsStatsSupported => AddressMap is not null;
    }
}
