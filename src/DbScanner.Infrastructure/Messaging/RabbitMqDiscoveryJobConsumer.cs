using System.Text.Json;
using DbScanner.Application.Ports;
using DbScanner.Contracts.Discovery;
using DbScanner.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DbScanner.Infrastructure.Messaging;

internal sealed class RabbitMqDiscoveryJobConsumer(
    RabbitMqConnectionFactory connectionFactory,
    IServiceScopeFactory serviceScopeFactory,
    IOptions<RabbitMqOptions> options,
    IOptions<TenantIsolationOptions> tenantOptions,
    ILogger<RabbitMqDiscoveryJobConsumer> logger) : IDiscoveryJobConsumer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.Create().CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await RabbitMqTopology.DeclareDiscoveryAsync(channel, options.Value, cancellationToken);
        await channel.BasicQosAsync(0, options.Value.PrefetchCount, false, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) => await HandleMessageAsync(channel, args, cancellationToken);
        await channel.BasicConsumeAsync(
            queue: options.Value.DiscoveryRequestedQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        logger.LogInformation("Discovery consumer started on queue {QueueName}.", options.Value.DiscoveryRequestedQueue);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Discovery consumer is stopping.");
        }
    }

    private async Task HandleMessageAsync(
        IChannel channel,
        BasicDeliverEventArgs args,
        CancellationToken cancellationToken)
    {
        try
        {
            var message = JsonSerializer.Deserialize<DiscoveryRequestedMessage>(args.Body.Span, JsonOptions)
                ?? throw new InvalidOperationException("Discovery message could not be deserialized.");
            if (!tenantOptions.Value.AcceptsMessage(message.TenantId))
            {
                await channel.BasicRejectAsync(args.DeliveryTag, requeue: false, cancellationToken);
                return;
            }
            using var scope = serviceScopeFactory.CreateScope();
            var agent = scope.ServiceProvider.GetRequiredService<IDiscoveryAgent>();
            await agent.ExecuteAsync(message.DiscoveryJobId, cancellationToken);
            await channel.BasicAckAsync(args.DeliveryTag, false, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Discovery job processing failed and was dead-lettered.");
            await channel.BasicNackAsync(args.DeliveryTag, false, requeue: false, cancellationToken);
        }
    }
}
