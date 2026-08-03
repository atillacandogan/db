using DbScanner.Application.Models;

namespace DbScanner.Application.Ports;

public interface INseScriptSyncService
{
    Task<NseScriptSyncResult> SyncInstalledScriptsAsync(CancellationToken cancellationToken = default);
}
