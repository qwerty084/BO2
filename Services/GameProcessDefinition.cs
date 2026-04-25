namespace BO2.Services
{
    public sealed record GameProcessDefinition(
        GameVariant Variant,
        string DisplayName,
        string ProcessName,
        string? CommandLineToken,
        PlayerStatAddressMap? AddressMap,
        string? UnsupportedReason);
}
