using System.Text;
using System.Text.Json;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Contracts.NseScripts;
using DbScanner.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DbScanner.Infrastructure.Messaging;

internal sealed class RabbitMqNseScriptSyncConsumer(
    RabbitMqConnectionFactory connectionFactory,
    IServiceScopeFactory serviceScopeFactory,
    IOptions<RabbitMqOptions> options,
    IOptions<TenantIsolationOptions> tenantOptions,
    ILogger<RabbitMqNseScriptSyncConsumer> logger) : INseScriptSyncConsumer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.Create().CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await RabbitMqTopology.DeclareNseScriptSyncAsync(channel, options.Value, cancellationToken);
        await channel.BasicQosAsync(0, 1, false, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) => await HandleMessageAsync(channel, args, cancellationToken);

        await channel.BasicConsumeAsync(
            queue: options.Value.NseScriptSyncQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        logger.LogInformation("NSE script sync consumer started on queue {QueueName}", options.Value.NseScriptSyncQueue);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("NSE script sync consumer is stopping.");
        }
    }

    private async Task HandleMessageAsync(
        IChannel channel,
        BasicDeliverEventArgs args,
        CancellationToken cancellationToken)
    {
        NseScriptSyncRequestedMessage? request = null;
        NseScriptSyncResponseMessage response;

        try
        {
            request = JsonSerializer.Deserialize<NseScriptSyncRequestedMessage>(args.Body.Span, JsonOptions)
                ?? throw new InvalidOperationException("NSE script sync request was empty.");
            if (!tenantOptions.Value.AcceptsMessage(request.TenantId))
            {
                await channel.BasicRejectAsync(args.DeliveryTag, requeue: false, cancellationToken);
                return;
            }

            using var scope = serviceScopeFactory.CreateScope();
            var syncService = scope.ServiceProvider.GetRequiredService<INseScriptSyncService>();
            var result = await syncService.SyncInstalledScriptsAsync(cancellationToken);
            response = MapResponse(request.RequestId, result);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "NSE script sync failed.");
            response = new NseScriptSyncResponseMessage(
                request?.RequestId ?? Guid.Empty,
                string.Empty,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                exception.Message);
        }

        response = response with { TenantId = tenantOptions.Value.TenantId };
        if (!string.IsNullOrWhiteSpace(args.BasicProperties.ReplyTo))
        {
            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response, JsonOptions));
            var properties = new BasicProperties
            {
                ContentType = "application/json",
                CorrelationId = args.BasicProperties.CorrelationId,
                MessageId = response.RequestId.ToString(),
                Type = nameof(NseScriptSyncResponseMessage)
            };

            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: args.BasicProperties.ReplyTo,
                mandatory: false,
                basicProperties: properties,
                body: body,
                cancellationToken: cancellationToken);
        }

        await channel.BasicAckAsync(args.DeliveryTag, false, cancellationToken);
    }

    private static NseScriptSyncResponseMessage MapResponse(Guid requestId, NseScriptSyncResult result) =>
        new(
            requestId,
            result.ScriptDirectory,
            result.DiscoveredCount,
            result.AddedCount,
            result.UpdatedCount,
            result.UnchangedCount,
            result.SkippedCount,
            result.FailedCount,
            result.UninstalledCount);
}
