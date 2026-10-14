using DbScanner.Application.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DbScanner.Infrastructure.Observability;

internal sealed class ScanCompatibilityReadinessMonitor(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ScanCompatibilityReadinessMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(
            1,
            configuration.GetValue("Telemetry:CompatibilityReadinessIntervalMinutes", 5)));

        await AuditAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await AuditAsync(stoppingToken);
        }
    }

    private async Task AuditAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var readinessService = scope.ServiceProvider
                .GetRequiredService<IScanCompatibilityReadinessService>();
            await readinessService.GetAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Scan-check compatibility readiness audit failed; legacy cleanup remains blocked.");
        }
    }
}
