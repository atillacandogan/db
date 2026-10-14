using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DbScanner.Infrastructure.Messaging;

internal sealed class ScanJobHeartbeat(
    IServiceScopeFactory scopes,
    IOptions<ScanReliabilityOptions> options,
    ILogger<ScanJobHeartbeat> logger)
{
    internal async Task RunAsync(ScanJobLease lease, CancellationTokenSource execution,
        CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.HeartbeatSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                using var scope = scopes.CreateScope();
                if (!await scope.ServiceProvider.GetRequiredService<IScanJobLeaseStore>()
                    .RenewAsync(lease, stoppingToken))
                {
                    await execution.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            // A database/network failure makes ownership uncertain. Stop executing;
            // the durable lease and recovery loop decide whether this job can retry.
            logger.LogWarning("Scan {ScanId} heartbeat failed ({ErrorType}); stopping execution.",
                lease.ScanId, exception.GetType().Name);
            await execution.CancelAsync();
        }
    }
}
