namespace DbScanner.Contracts.NseScripts;

public sealed record NseScriptSyncRequestedMessage(Guid RequestId, string? TenantId = null);
