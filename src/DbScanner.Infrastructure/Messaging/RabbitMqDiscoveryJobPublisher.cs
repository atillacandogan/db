using System.Text;
using System.Text.Json;
using DbScanner.Application.Ports;
using DbScanner.Contracts.Discovery;
using DbScanner.Domain.Entities;
using DbScanner.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace DbScanner.Infrastructure.Messaging;

internal sealed class RabbitMqDiscoveryJobPublisher(
    RabbitMqConnectionFactory connectionFactory,
    IOptions<RabbitMqOptions> options,
    IOptions<TenantIsolationOptions> tenantOptions) : IDiscoveryJobPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task PublishRequestedAsync(DiscoveryJob job, CancellationToken cancellationToken = default)
    {
        var message = new DiscoveryRequestedMessage(job.Id, DateTimeOffset.UtcNow, tenantOptions.Value.TenantId);
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, JsonOptions));

        await using var connection = await connectionFactory.Create().CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await RabbitMqTopology.DeclareDiscoveryAsync(channel, options.Value, cancellationToken);

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: options.Value.DiscoveryRequestedQueue,
            mandatory: true,
            basicProperties: new BasicProperties
            {
                ContentType = "application/json",
                MessageId = job.Id.ToString(),
                Persistent = true,
                Type = nameof(DiscoveryRequestedMessage)
            },
            body: body,
            cancellationToken: cancellationToken);
    }
}
