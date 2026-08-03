namespace DbScanner.Contracts.Scans;

public sealed record ScanTargetDto(
    Guid Id,
    string Name,
    string Host,
    int Port,
    string Engine);
