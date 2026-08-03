namespace DbScanner.Contracts.Scans;

public sealed record ScanFindingDto(
    Guid Id,
    Guid ScanId,
    Guid TargetId,
    string RuleId,
    string Title,
    string Severity,
    int ScoreContribution,
    string Evidence,
    string Remediation,
    string SourceScript,
    DateTimeOffset DetectedAt);
