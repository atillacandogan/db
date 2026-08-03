using RabbitMQ.Client;

namespace DbScanner.Infrastructure.Messaging;

internal static class RabbitMqTopology
{
    public static async Task DeclareAsync(
        IChannel channel,
        RabbitMqOptions options,
        CancellationToken cancellationToken)
    {
        var requestedArgs = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = string.Empty,
            ["x-dead-letter-routing-key"] = options.DeadLetterQueue
        };

        var retryArgs = new Dictionary<string, object?>
        {
            ["x-message-ttl"] = options.RetryDelayMilliseconds,
            ["x-dead-letter-exchange"] = string.Empty,
            ["x-dead-letter-routing-key"] = options.RequestedQueue
        };

        await channel.QueueDeclareAsync(
            queue: options.DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: options.RetryQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: retryArgs,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: options.RequestedQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: requestedArgs,
            cancellationToken: cancellationToken);
    }

    public static Task DeclareNseScriptSyncAsync(
        IChannel channel,
        RabbitMqOptions options,
        CancellationToken cancellationToken) =>
        channel.QueueDeclareAsync(
            queue: options.NseScriptSyncQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

    public static async Task DeclareDiscoveryAsync(
        IChannel channel,
        RabbitMqOptions options,
        CancellationToken cancellationToken)
    {
        await channel.QueueDeclareAsync(
            queue: options.DiscoveryDeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: options.DiscoveryRequestedQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = options.DiscoveryDeadLetterQueue
            },
            cancellationToken: cancellationToken);
    }
}
