namespace DbScanner.Contracts.Findings;

public sealed record FindingListItemDto(
    Guid Id,
    Guid ScanId,
    Guid TargetId,
    string TargetName,
    string TargetHost,
    string TargetEngine,
    string RuleId,
    string Title,
    string Severity,
    int ScoreContribution,
    string Evidence,
    string Remediation,
    string SourceScript,
    string ScanStatus,
    int? RiskScore,
    string? OverallSeverity,
    DateTimeOffset DetectedAt);
