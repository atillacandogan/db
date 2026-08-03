using System.Text;
using System.Text.Json;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Contracts.Scans;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace DbScanner.Infrastructure.Messaging;

internal sealed class RabbitMqScanJobPublisher(
    RabbitMqConnectionFactory connectionFactory,
    IOptions<RabbitMqOptions> options,
    IScanCredentialProtector credentialProtector) : IScanJobPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task PublishScanRequestedAsync(Scan scan, Target target, CancellationToken cancellationToken = default) =>
        PublishScanRequestedAsync(scan, target, selectedNseScriptIds: null, authentication: null, cancellationToken);

    public async Task PublishScanRequestedAsync(
        Scan scan,
        Target target,
        IReadOnlyCollection<string>? selectedNseScriptIds,
        CancellationToken cancellationToken) =>
        await PublishScanRequestedAsync(scan, target, selectedNseScriptIds, authentication: null, cancellationToken);

    public async Task PublishScanRequestedAsync(
        Scan scan,
        Target target,
        IReadOnlyCollection<string>? selectedNseScriptIds,
        DatabaseAuthentication? authentication,
        CancellationToken cancellationToken)
    {
        var protectedCredentials = authentication is null
            ? null
            : credentialProtector.Protect(authentication);
        var message = new ScanRequestedMessage(
            scan.Id,
            target.Id,
            target.Engine.ToApiString(),
            target.Host,
            target.Port,
            DateTimeOffset.UtcNow,
            selectedNseScriptIds?.ToArray(),
            protectedCredentials);

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, JsonOptions));

        await using var connection = await connectionFactory.Create().CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await RabbitMqTopology.DeclareAsync(channel, options.Value, cancellationToken);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            MessageId = scan.Id.ToString(),
            Persistent = true,
            Type = nameof(ScanRequestedMessage)
        };

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: options.Value.RequestedQueue,
            mandatory: true,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }
}
