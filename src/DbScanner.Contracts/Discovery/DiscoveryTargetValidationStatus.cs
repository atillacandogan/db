namespace DbScanner.Contracts.Discovery;

public static class DiscoveryTargetValidationStatus
{
    public const string Authorized = "Authorized";
    public const string InvalidFormat = "InvalidFormat";
    public const string NotInAllowlist = "NotInAllowlist";
    public const string InactiveAllowlist = "InactiveAllowlist";
    public const string EnvironmentMismatch = "EnvironmentMismatch";
    public const string UnsupportedAddressType = "UnsupportedAddressType";
    public const string CidrTooLarge = "CidrTooLarge";
    public const string DnsResolutionFailed = "DnsResolutionFailed";
    public const string ResolvedAddressNotAuthorized = "ResolvedAddressNotAuthorized";
}
