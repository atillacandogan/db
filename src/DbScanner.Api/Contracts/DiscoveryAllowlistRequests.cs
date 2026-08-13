namespace DbScanner.Api.Contracts;

public sealed record CreateDiscoveryAllowlistRequest(
    string Name,
    string Environment,
    string TargetType,
    string TargetValue,
    IReadOnlyList<string> AllowedEngines,
    IReadOnlyDictionary<string, IReadOnlyList<int>>? AllowedPorts,
    bool IsActive,
    string? Description);

public sealed record UpdateDiscoveryAllowlistRequest(
    string Name,
    string Environment,
    string TargetType,
    string TargetValue,
    IReadOnlyList<string> AllowedEngines,
    IReadOnlyDictionary<string, IReadOnlyList<int>>? AllowedPorts,
    bool IsActive,
    string? Description);

public sealed record SetDiscoveryAllowlistActiveRequest(bool IsActive);

public sealed record ValidateDiscoveryScopeRequest(
    string Scope,
    string Environment,
    IReadOnlyList<string>? SupportedEngineFilter = null);
