using DbScanner.Domain.Enums;

namespace DbScanner.Domain.Entities;

public sealed class Scan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TargetId { get; set; }
    public Target? Target { get; set; }
    public ScanStatus Status { get; set; } = ScanStatus.Pending;
    public int? RiskScore { get; set; }
    public Severity? OverallSeverity { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset? TimeoutAt { get; set; }
    public string? ErrorMessage { get; set; }

    public ICollection<ScanScriptResult> ScriptResults { get; } = new List<ScanScriptResult>();
    public ICollection<Finding> Findings { get; } = new List<Finding>();
}
