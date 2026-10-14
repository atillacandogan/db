using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DbScanner.Infrastructure.Messaging;

internal sealed class ScanOutboxDispatcher(
    IServiceScopeFactory scopes,
    IScanOutboxTransport transport,
    IOptions<ScanReliabilityOptions> options,
    ILogger<ScanOutboxDispatcher> logger) : BackgroundService
{
    internal async Task<bool> DispatchOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ScanJobReliabilityStore>();
        var entry = await store.ClaimOutboxAsync(cancellationToken);
        if (entry is null) return false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.PublishTimeoutSeconds));
            await transport.PublishAsync(entry, timeout.Token);
            await store.MarkPublishedAsync(entry, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning("Scan {ScanId} publish failed ({ErrorType}); durable request retained.",
                entry.ScanId, exception.GetType().Name);
            await store.ReschedulePublishAsync(entry, cancellationToken);
        }
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await DispatchOnceAsync(stoppingToken)) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning("Outbox polling failed ({ErrorType}); will retry.", exception.GetType().Name);
            }
            await Task.Delay(TimeSpan.FromSeconds(options.Value.OutboxPollSeconds), stoppingToken);
        }
    }
}
