namespace DbScanner.Contracts.Scans;

public sealed record ScanRequestedMessage(
    Guid ScanId,
    Guid TargetId,
    string Engine,
    string Host,
    int Port,
    DateTimeOffset RequestedAt,
    IReadOnlyList<string>? SelectedNseScriptIds = null,
    string? ProtectedCredentials = null);
