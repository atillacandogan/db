namespace DbScanner.Contracts.Scans;

public sealed record ScanListItemDto(
    Guid Id,
    Guid TargetId,
    string Status,
    int? RiskScore,
    string? OverallSeverity,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    ScanTargetDto? Target);
