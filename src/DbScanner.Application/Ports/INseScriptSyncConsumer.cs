namespace DbScanner.Application.Ports;

public interface INseScriptSyncConsumer
{
    Task StartAsync(CancellationToken cancellationToken = default);
}
