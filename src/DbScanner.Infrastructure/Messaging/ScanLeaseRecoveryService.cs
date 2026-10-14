using DbScanner.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DbScanner.Infrastructure.Messaging;

internal sealed class ScanLeaseRecoveryService(
    IServiceScopeFactory scopes,
    IOptions<ScanReliabilityOptions> options,
    ILogger<ScanLeaseRecoveryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var recovered = await scope.ServiceProvider.GetRequiredService<IScanJobLeaseStore>()
                    .RecoverExpiredAsync(stoppingToken);
                if (recovered > 0) logger.LogWarning("Recovered {Count} expired scan leases.", recovered);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning("Scan lease recovery failed ({ErrorType}); will retry.", exception.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(options.Value.RecoveryIntervalSeconds), stoppingToken);
        }
    }
}
