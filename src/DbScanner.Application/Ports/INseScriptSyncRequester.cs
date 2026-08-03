using DbScanner.Application.Models;

namespace DbScanner.Application.Ports;

public interface INseScriptSyncRequester
{
    Task<NseScriptSyncResult> RequestSyncAsync(CancellationToken cancellationToken = default);
}
