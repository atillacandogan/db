namespace DbScanner.Contracts.Scans;

public sealed record ScanDto(
    Guid Id,
    Guid TargetId,
    string Status,
    int? RiskScore,
    string? OverallSeverity,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    ScanTargetDto? Target,
    IReadOnlyList<ScanScriptResultDto> ScriptResults,
    IReadOnlyList<ScanFindingDto> Findings);
