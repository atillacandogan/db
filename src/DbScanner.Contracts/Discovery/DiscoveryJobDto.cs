namespace DbScanner.Contracts.Discovery;

public sealed record DiscoveryJobDto(
    Guid Id,
    string Name,
    string Scope,
    string Environment,
    IReadOnlyList<string> SupportedEngineFilter,
    string Status,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyList<DiscoveryResultDto> SupportedDatabases,
    IReadOnlyList<DiscoveryResultDto> UnsupportedDatabases);
