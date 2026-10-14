using System.Text.Json;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Persistence;
using DbScanner.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DbScanner.Infrastructure.Messaging;

// This adapter stages the request; the application's scoped repository commits it with the scan.
internal sealed class OutboxScanJobPublisher(
    DbScannerDbContext dbContext,
    IScanCredentialProtector credentialProtector,
    IOptions<TenantIsolationOptions> tenantOptions) : IScanJobPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task PublishScanRequestedAsync(Scan scan, Target target, CancellationToken cancellationToken = default) =>
        PublishScanRequestedAsync(scan, target, null, null, cancellationToken);

    public Task PublishScanRequestedAsync(Scan scan, Target target,
        IReadOnlyCollection<string>? selectedCheckIds, CancellationToken cancellationToken) =>
        PublishScanRequestedAsync(scan, target, selectedCheckIds, null, cancellationToken);

    public async Task PublishScanRequestedAsync(Scan scan, Target target,
        IReadOnlyCollection<string>? selectedCheckIds, DatabaseAuthentication? authentication,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Initial availability must use the same clock as dispatch/retry/lease decisions.
        // A host clock ahead of PostgreSQL would otherwise delay a newly committed request.
        var now = await dbContext.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"")
            .SingleAsync(cancellationToken);
        var message = ScanRequestedMessageCompatibility.Create(
            scan.Id, target.Id, target.Engine.ToApiString(), target.Host, target.Port,
            now, selectedCheckIds,
            authentication is null ? null : credentialProtector.Protect(authentication))
            with { TenantId = tenantOptions.Value.TenantId };
        dbContext.ScanOutboxMessages.Add(new ScanOutboxMessage
        {
            ScanId = scan.Id,
            Payload = JsonSerializer.Serialize(message, JsonOptions),
            CreatedAt = now,
            AvailableAt = now
        });
    }
}
