namespace DbScanner.Contracts.NseScripts;

public sealed record NseScriptSyncResponseMessage(
    Guid RequestId,
    string ScriptDirectory,
    int DiscoveredCount,
    int AddedCount,
    int UpdatedCount,
    int UnchangedCount,
    int SkippedCount,
    int FailedCount,
    int UninstalledCount,
    string? Error = null);
