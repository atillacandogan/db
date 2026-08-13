namespace DbScanner.Contracts.Discovery;

public sealed record DiscoveryScopeValidationDto(
    string Environment,
    bool IsValid,
    IReadOnlyList<DiscoveryTargetValidationDto> Targets);
