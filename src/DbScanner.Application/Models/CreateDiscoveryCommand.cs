using DbScanner.Domain.Enums;

namespace DbScanner.Application.Models;

public sealed record CreateDiscoveryCommand(
    string Name,
    string? Scope,
    string Environment,
    IReadOnlyCollection<DatabaseEngine> SupportedEngineFilter,
    bool UseAuthorizedScope = true);
