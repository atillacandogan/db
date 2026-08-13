namespace DbScanner.Contracts.Discovery;

public sealed record DiscoveryAllowlistEntryDto(
    Guid Id,
    string Name,
    string Environment,
    string TargetType,
    string TargetValue,
    IReadOnlyList<string> AllowedEngines,
    IReadOnlyDictionary<string, IReadOnlyList<int>> AllowedPorts,
    bool IsActive,
    string? Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
