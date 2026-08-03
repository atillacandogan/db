namespace DbScanner.Application.Models;

public sealed record NseScriptSyncResult(
    string ScriptDirectory,
    int DiscoveredCount,
    int AddedCount,
    int UpdatedCount,
    int UnchangedCount,
    int UninstalledCount);
