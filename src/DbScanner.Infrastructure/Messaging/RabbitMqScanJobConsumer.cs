using System.Text;
using System.Text.Json;
using DbScanner.Application.Ports;
using DbScanner.Contracts.Scans;
using DbScanner.Domain.Enums;
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
    ILogger<RabbitMqScanJobConsumer> logger) : IScanJobConsumer
{
    private const string RetryCountHeader = "x-retry-count";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.Create().CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await RabbitMqTopology.DeclareAsync(channel, options.Value, cancellationToken);
        await channel.BasicQosAsync(0, options.Value.PrefetchCount, false, cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, args) =>
        {
            await HandleMessageAsync(channel, args, cancellationToken);
        };

        await channel.BasicConsumeAsync(
            queue: options.Value.RequestedQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        logger.LogInformation("Scan job consumer started on queue {QueueName}", options.Value.RequestedQueue);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Scan job consumer is stopping.");
        }
    }

    private async Task HandleMessageAsync(
        IChannel channel,
        BasicDeliverEventArgs args,
        CancellationToken cancellationToken)
    {
        ScanRequestedMessage? message = null;

        try
        {
            message = JsonSerializer.Deserialize<ScanRequestedMessage>(args.Body.Span, JsonOptions);
            if (message is null)
            {
                await PublishDeadLetterAsync(channel, args.Body.ToArray(), args, "Message body could not be deserialized.", cancellationToken);
                await channel.BasicAckAsync(args.DeliveryTag, false, cancellationToken);
                return;
            }

            using var scope = serviceScopeFactory.CreateScope();
            var scanRepository = scope.ServiceProvider.GetRequiredService<IScanRepository>();
            var scanProcessingService = scope.ServiceProvider.GetRequiredService<IScanProcessingService>();

            var markedScanning = await scanRepository.TryMarkScanningAsync(message.ScanId, cancellationToken);

            if (!markedScanning)
            {
                logger.LogInformation("Scan job {ScanId} was ignored because it is not pending.", message.ScanId);
                await channel.BasicAckAsync(args.DeliveryTag, false, cancellationToken);
                return;
            }

            var authentication = string.IsNullOrWhiteSpace(message.ProtectedCredentials)
                ? null
                : credentialProtector.Unprotect(message.ProtectedCredentials);

            await scanProcessingService.ProcessAsync(
                message.ScanId,
                message.SelectedNseScriptIds,
                authentication,
                cancellationToken);

            var processedScan = await scanRepository.GetByIdAsync(message.ScanId, cancellationToken);
            logger.LogInformation(
                "Scan job {ScanId} finished with status {Status}.",
                message.ScanId,
                processedScan?.Status ?? ScanStatus.Scanning);

            await channel.BasicAckAsync(args.DeliveryTag, false, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Scan job processing failed.");

            if (message is not null)
            {
                using var scope = serviceScopeFactory.CreateScope();
                var scanRepository = scope.ServiceProvider.GetRequiredService<IScanRepository>();
                await scanRepository.IncrementRetryCountAsync(message.ScanId, cancellationToken);
                await scanRepository.SaveChangesAsync(cancellationToken);
            }

            await RetryOrDeadLetterAsync(channel, args, exception.Message, cancellationToken);
            await channel.BasicAckAsync(args.DeliveryTag, false, cancellationToken);
        }
    }

    private async Task RetryOrDeadLetterAsync(
        IChannel channel,
        BasicDeliverEventArgs args,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var retryCount = GetRetryCount(args.BasicProperties.Headers);
        if (retryCount >= options.Value.MaxRetryCount)
        {
            await PublishDeadLetterAsync(channel, args.Body.ToArray(), args, errorMessage, cancellationToken);
            return;
        }

        var properties = CreateProperties(args, retryCount + 1, errorMessage);
        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: options.Value.RetryQueue,
            mandatory: true,
            basicProperties: properties,
            body: args.Body,
            cancellationToken: cancellationToken);
    }

    private async Task PublishDeadLetterAsync(
        IChannel channel,
        byte[] body,
        BasicDeliverEventArgs args,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        var properties = CreateProperties(args, GetRetryCount(args.BasicProperties.Headers), errorMessage);
        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: options.Value.DeadLetterQueue,
            mandatory: true,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }

    private static BasicProperties CreateProperties(
        BasicDeliverEventArgs args,
        int retryCount,
        string errorMessage)
    {
        var headers = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [RetryCountHeader] = retryCount,
            ["x-error"] = errorMessage
        };

        return new BasicProperties
        {
            ContentType = args.BasicProperties.ContentType ?? "application/json",
            MessageId = args.BasicProperties.MessageId,
            Persistent = true,
            Type = args.BasicProperties.Type,
            Headers = headers
        };
    }

    private static int GetRetryCount(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue(RetryCountHeader, out var value) || value is null)
        {
            return 0;
        }

        return value switch
        {
            int intValue => intValue,
            long longValue => checked((int)longValue),
            byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            _ => 0
        };
    }
}
