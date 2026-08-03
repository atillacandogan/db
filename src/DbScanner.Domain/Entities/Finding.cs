using DbScanner.Domain.Enums;

namespace DbScanner.Domain.Entities;

public sealed class Finding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScanId { get; set; }
    public Scan? Scan { get; set; }
    public Guid TargetId { get; set; }
    public Target? Target { get; set; }
    public required string RuleId { get; set; }
    public Rule? Rule { get; set; }
    public required string Title { get; set; }
    public Severity Severity { get; set; }
    public int ScoreContribution { get; set; }
    public FindingStatus Status { get; set; } = FindingStatus.Open;
    public required string Evidence { get; set; }
    public required string Remediation { get; set; }
    public required string SourceScript { get; set; }
    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
}
