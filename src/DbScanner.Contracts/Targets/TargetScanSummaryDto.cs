namespace DbScanner.Contracts.Targets;

public sealed record TargetScanSummaryDto(
    Guid ScanId,
    string Status,
    int? RiskScore,
    string? OverallSeverity,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt);
