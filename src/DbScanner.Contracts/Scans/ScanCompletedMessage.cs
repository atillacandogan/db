namespace DbScanner.Contracts.Scans;

public sealed record ScanCompletedMessage(
    Guid ScanId,
    string Status,
    int? RiskScore,
    string? OverallSeverity,
    DateTimeOffset CompletedAt,
    string? ErrorMessage);
