namespace DbScanner.Application.Models;

public sealed record UpsertDiscoveryAllowlistCommand(
    string Name,
    string Environment,
    string TargetType,
    string TargetValue,
    IReadOnlyCollection<string> AllowedEngines,
    IReadOnlyDictionary<string, IReadOnlyList<int>>? AllowedPorts,
    bool IsActive,
    string? Description);
