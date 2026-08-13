namespace DbScanner.Contracts.Discovery;

public sealed record DiscoveryTargetValidationDto(
    string Target,
    string Status,
    string Message,
    IReadOnlyList<string> ResolvedAddresses);
