using System.Text;
using System.Text.Json;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Contracts.NseScripts;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace DbScanner.Infrastructure.Messaging;

internal sealed class RabbitMqNseScriptSyncRequester(
    RabbitMqConnectionFactory connectionFactory,
    IOptions<RabbitMqOptions> options) : INseScriptSyncRequester
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<NseScriptSyncResult> RequestSyncAsync(CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid();
        var completion = new TaskCompletionSource<NseScriptSyncResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var connection = await connectionFactory.Create().CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await RabbitMqTopology.DeclareNseScriptSyncAsync(channel, options.Value, cancellationToken);
        var replyQueue = await channel.QueueDeclareAsync(
            queue: string.Empty,
            durable: false,
            exclusive: true,
            autoDelete: true,
            arguments: null,
            cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, args) =>
        {
            if (!string.Equals(args.BasicProperties.CorrelationId, requestId.ToString(), StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }

            try
            {
                var response = JsonSerializer.Deserialize<NseScriptSyncResponseMessage>(args.Body.Span, JsonOptions)
                    ?? throw new InvalidOperationException("NSE script sync response was empty.");
                completion.TrySetResult(response);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }

            return Task.CompletedTask;
        };

        await channel.BasicConsumeAsync(
            queue: replyQueue.QueueName,
            autoAck: true,
            consumer: consumer,
            cancellationToken: cancellationToken);

        var request = new NseScriptSyncRequestedMessage(requestId);
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, JsonOptions));
        var properties = new BasicProperties
        {
            ContentType = "application/json",
            CorrelationId = requestId.ToString(),
            MessageId = requestId.ToString(),
            ReplyTo = replyQueue.QueueName,
            Persistent = true,
            Type = nameof(NseScriptSyncRequestedMessage)
        };

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: options.Value.NseScriptSyncQueue,
            mandatory: true,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.NseScriptSyncTimeoutSeconds));
        using var registration = timeout.Token.Register(() => completion.TrySetCanceled(timeout.Token));

        NseScriptSyncResponseMessage response;
        try
        {
            response = await completion.Task;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Scanner Worker did not complete NSE script sync in time.");
        }

        if (!string.IsNullOrWhiteSpace(response.Error))
        {
            throw new InvalidOperationException(response.Error);
        }

        return new NseScriptSyncResult(
            response.ScriptDirectory,
            response.DiscoveredCount,
            response.AddedCount,
            response.UpdatedCount,
            response.UnchangedCount,
            response.SkippedCount,
            response.FailedCount,
            response.UninstalledCount);
    }
}
