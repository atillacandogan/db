using System.Text.Json.Serialization;

namespace DbScanner.Contracts.Scans;

public sealed record ScanRequestedMessage(
    Guid ScanId,
    Guid TargetId,
    string Engine,
    string Host,
    int Port,
    DateTimeOffset RequestedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<string>? SelectedNseScriptIds = null,
    string? ProtectedCredentials = null,
    IReadOnlyList<string>? SelectedCheckIds = null,
    string? TenantId = null);
