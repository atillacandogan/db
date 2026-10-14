namespace DbScanner.Api.Contracts;

public sealed record CreateScanRequest(
    Guid TargetId,
    IReadOnlyList<string>? SelectedNseScriptIds = null,
    IReadOnlyList<string>? SelectedCheckIds = null);
