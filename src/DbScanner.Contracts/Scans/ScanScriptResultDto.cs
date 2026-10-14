namespace DbScanner.Contracts.Scans;

public sealed record ScanScriptResultDto(
    Guid Id,
    string ScriptName,
    string? RawXml,
    string? NormalizedOutput,
    int? ExitCode,
    string? ResultStatus,
    DateTimeOffset CreatedAt,
    string? ScanCheckId = null);
