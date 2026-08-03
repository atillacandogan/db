using DbScanner.Application.Ports;

namespace DbScanner.ScannerWorker;

public class Worker(
    IScanJobConsumer scanJobConsumer,
    INseScriptSyncConsumer nseScriptSyncConsumer,
    IDiscoveryJobConsumer discoveryJobConsumer,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DbScanner worker starting.");
        await SyncInstalledNseScriptsAsync(stoppingToken);
        await Task.WhenAll(
            scanJobConsumer.StartAsync(stoppingToken),
            nseScriptSyncConsumer.StartAsync(stoppingToken),
            discoveryJobConsumer.StartAsync(stoppingToken));
    }

    private async Task SyncInstalledNseScriptsAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<INseScriptSyncService>();
        var result = await syncService.SyncInstalledScriptsAsync(cancellationToken);

        logger.LogInformation(
            "NSE scripts synced from {ScriptDirectory}. Discovered: {DiscoveredCount}, added: {AddedCount}, updated: {UpdatedCount}, unchanged: {UnchangedCount}, uninstalled: {UninstalledCount}.",
            result.ScriptDirectory,
            result.DiscoveredCount,
            result.AddedCount,
            result.UpdatedCount,
            result.UnchangedCount,
            result.UninstalledCount);
    }
}
