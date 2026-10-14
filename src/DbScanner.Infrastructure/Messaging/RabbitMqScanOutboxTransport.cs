using System.Text;
using DbScanner.Contracts.Scans;
using System.Text.Json;
using DbScanner.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace DbScanner.Infrastructure.Messaging;

internal interface IScanOutboxTransport
{
    Task PublishAsync(ClaimedScanOutbox entry, CancellationToken cancellationToken);
}

// Dispatcher is sequential; this adapter reuses its confirm-enabled channel.
internal sealed class RabbitMqScanOutboxTransport(
    RabbitMqConnectionFactory connectionFactory,
    IOptions<RabbitMqOptions> options,
    IOptions<TenantIsolationOptions> tenantOptions) : IScanOutboxTransport, IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private IConnection? connection;
    private IChannel? channel;

    public async Task PublishAsync(ClaimedScanOutbox entry, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<ScanRequestedMessage>(entry.Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (request is null || request.ScanId != entry.ScanId || !tenantOptions.Value.AcceptsMessage(request.TenantId))
            throw new InvalidOperationException("Outbox request tenant/scan identity is invalid.");
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (channel is null || !channel.IsOpen || connection is null || !connection.IsOpen)
            {
                await ResetAsync();
                connection = await connectionFactory.Create().CreateConnectionAsync(cancellationToken);
                channel = await connection.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true,
                        publisherConfirmationTrackingEnabled: true), cancellationToken);
                await RabbitMqTopology.DeclareAsync(channel, options.Value, cancellationToken);
            }
            await channel.BasicPublishAsync(exchange: string.Empty,
                routingKey: entry.DeadLetter ? options.Value.DeadLetterQueue : options.Value.RequestedQueue,
                mandatory: true,
                basicProperties: new BasicProperties
                {
                    ContentType = "application/json",
                    MessageId = entry.ScanId.ToString(),
                    Persistent = true,
                    Type = nameof(ScanRequestedMessage)
                }, body: Encoding.UTF8.GetBytes(entry.Payload), cancellationToken: cancellationToken);
        }
        catch
        {
            // A timeout/uncertain confirm must retry through the outbox, never mark sent.
            await ResetAsync();
            throw;
        }
        finally { gate.Release(); }
    }

    private async Task ResetAsync()
    {
        if (channel is not null) await channel.DisposeAsync();
        if (connection is not null) await connection.DisposeAsync();
        channel = null;
        connection = null;
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try { await ResetAsync(); }
        finally { gate.Release(); gate.Dispose(); }
    }
}
