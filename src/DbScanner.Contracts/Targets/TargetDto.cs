namespace DbScanner.Contracts.Targets;

public sealed record TargetDto(
    Guid Id,
    string Name,
    string Host,
    int Port,
    string Engine,
    string ConnectionMode,
    string? EnvironmentTag,
    bool HasCredentials,
    bool Enabled,
    TargetScanSummaryDto? LastScan);
