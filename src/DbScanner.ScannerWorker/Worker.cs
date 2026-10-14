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
        try
        {
            await SyncInstalledNseScriptsAsync(stoppingToken);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(
                exception,
                "Initial NSE script synchronization failed. The worker will continue so synchronization can be retried.");
        }

        await Task.WhenAll(
            RunConsumerWithRecoveryAsync(scanJobConsumer.StartAsync, "scan", stoppingToken),
            RunConsumerWithRecoveryAsync(nseScriptSyncConsumer.StartAsync, "script-sync", stoppingToken),
            RunConsumerWithRecoveryAsync(discoveryJobConsumer.StartAsync, "discovery", stoppingToken));
    }

    private async Task RunConsumerWithRecoveryAsync(Func<CancellationToken, Task> start,
        string name, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await start(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                logger.LogWarning("{Consumer} consumer disconnected ({ErrorType}); reconnecting.",
                    name, exception.GetType().Name);
            }
            if (!stoppingToken.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task SyncInstalledNseScriptsAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<INseScriptSyncService>();
        var result = await syncService.SyncInstalledScriptsAsync(cancellationToken);

        logger.LogInformation(
            "NSE scripts synced from {ScriptDirectory}. Discovered: {DiscoveredCount}, added: {AddedCount}, updated: {UpdatedCount}, unchanged: {UnchangedCount}, skipped: {SkippedCount}, failed: {FailedCount}, uninstalled: {UninstalledCount}.",
            result.ScriptDirectory,
            result.DiscoveredCount,
            result.AddedCount,
            result.UpdatedCount,
            result.UnchangedCount,
            result.SkippedCount,
            result.FailedCount,
            result.UninstalledCount);
    }
}
