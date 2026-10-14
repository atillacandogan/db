using System.Text.Json;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Contracts.Scans;
using DbScanner.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DbScanner.Infrastructure.Messaging;

internal sealed class RabbitMqScanJobConsumer(
    RabbitMqConnectionFactory connectionFactory,
    IServiceScopeFactory serviceScopeFactory,
    IOptions<RabbitMqOptions> options,
    IScanCredentialProtector credentialProtector,
    ScanJobHeartbeat heartbeat,
    IOptions<TenantIsolationOptions> tenantOptions,
    ILogger<RabbitMqScanJobConsumer> logger) : IScanJobConsumer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.Create().CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await RabbitMqTopology.DeclareAsync(channel, options.Value, cancellationToken);
        await channel.BasicQosAsync(0, options.Value.PrefetchCount, false, cancellationToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, args) => HandleMessageAsync(channel, args, cancellationToken);
        await channel.BasicConsumeAsync(queue: options.Value.RequestedQueue, autoAck: false,
            consumer: consumer, cancellationToken: cancellationToken);
        logger.LogInformation("Scan job consumer started on queue {QueueName}", options.Value.RequestedQueue);
        try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    internal async Task HandleMessageAsync(IChannel channel, BasicDeliverEventArgs args,
        CancellationToken cancellationToken)
    {
        ScanJobLease? lease = null;
        try
        {
            var delivered = JsonSerializer.Deserialize<ScanRequestedMessage>(args.Body.Span, JsonOptions);
            if (delivered is null || delivered.ScanId == Guid.Empty
                || !tenantOptions.Value.AcceptsMessage(delivered.TenantId))
            {
                await channel.BasicRejectAsync(args.DeliveryTag, requeue: false, cancellationToken);
                return;
            }
            using var scope = serviceScopeFactory.CreateScope();
            lease = await scope.ServiceProvider.GetRequiredService<IScanJobLeaseStore>()
                .TryAcquireAsync(delivered, cancellationToken);
            if (lease is null)
            {
                // Terminal jobs and concurrent duplicate deliveries cannot execute.
                await channel.BasicAckAsync(args.DeliveryTag, false, cancellationToken);
                return;
            }
            var fence = scope.ServiceProvider.GetRequiredService<ScanExecutionLeaseContext>();
            fence.ScanId = lease.ScanId;
            fence.Token = lease.Token;
            using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var heartbeatTask = heartbeat.RunAsync(lease, execution, heartbeatStop.Token);
            try
            {
                // Always use the authoritative request snapshot, not a later delivery's payload.
                var request = lease.Request;
                if (!tenantOptions.Value.AcceptsMessage(request.TenantId))
                    throw new InvalidOperationException("Durable request belongs to a different tenant.");
                var authentication = string.IsNullOrWhiteSpace(request.ProtectedCredentials)
                    ? null : credentialProtector.Unprotect(request.ProtectedCredentials);
                await scope.ServiceProvider.GetRequiredService<IScanProcessingService>().ProcessAsync(
                    lease.ScanId, ScanRequestedMessageCompatibility.ResolveSelection(request),
                    authentication, execution.Token);
                logger.LogInformation("Scan job {ScanId} attempt {Attempt} finished.", lease.ScanId, lease.Attempt);
            }
            finally
            {
                await heartbeatStop.CancelAsync();
                await heartbeatTask;
            }
        }
        catch (JsonException)
        {
            await channel.BasicRejectAsync(args.DeliveryTag, requeue: false, cancellationToken);
            return;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // No ACK on shutdown. Broker redelivery and durable lease recovery remain available.
            return;
        }
        catch (ScanLeaseLostException)
        {
            logger.LogInformation("Scan {ScanId} no longer owns its lease; stale completion rejected.", lease?.ScanId);
        }
        catch (Exception exception)
        {
            logger.LogWarning("Scan {ScanId} interrupted ({ErrorType}); using durable recovery.",
                lease?.ScanId, exception.GetType().Name);
            if (lease is null)
            {
                // Acquisition could not commit. Do not acknowledge a request we did not own.
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                await channel.BasicNackAsync(args.DeliveryTag, false, requeue: true, cancellationToken);
                return;
            }
            using var retryScope = serviceScopeFactory.CreateScope();
            await retryScope.ServiceProvider.GetRequiredService<IScanJobLeaseStore>()
                .RetryAsync(lease, cancellationToken);
        }
        // Processing is committed, requeued durably, or ownership has moved to recovery.
        await channel.BasicAckAsync(args.DeliveryTag, false, cancellationToken);
    }
}
