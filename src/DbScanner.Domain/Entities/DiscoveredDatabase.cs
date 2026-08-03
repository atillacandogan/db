using DbScanner.Domain.Enums;

namespace DbScanner.Domain.Entities;

public sealed class DiscoveredDatabase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DiscoveryJobId { get; set; }
    public DiscoveryJob? DiscoveryJob { get; set; }
    public required string Host { get; set; }
    public int Port { get; set; }
    public required string DetectedService { get; set; }
    public DatabaseEngine? DetectedEngine { get; set; }
    public int Confidence { get; set; }
    public DiscoveryResultStatus Status { get; set; }
    public required string Reason { get; set; }
    public Guid? TargetId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
