using DbScanner.Domain.Enums;

namespace DbScanner.Domain.Entities;

public sealed class DiscoveryJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string ScopeInput { get; set; }
    public required string EnvironmentTag { get; set; }
    public required string SupportedEngineFilter { get; set; }
    public DiscoveryStatus Status { get; set; } = DiscoveryStatus.Pending;
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }

    public ICollection<DiscoveredDatabase> Results { get; } = new List<DiscoveredDatabase>();
}
