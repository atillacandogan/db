using System.Text.Json;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Contracts.Scans;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DbScanner.Infrastructure.Messaging;

internal sealed record ClaimedScanOutbox(Guid Id, Guid ScanId, Guid Token, string Payload, bool DeadLetter);

/// <summary>PostgreSQL is the authority for time, ownership and durable replay.</summary>
internal sealed class ScanJobReliabilityStore(
    DbScannerDbContext dbContext,
    IOptions<ScanReliabilityOptions> options,
    ResourceAccessContext access) : IScanJobLeaseStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private Task<DateTimeOffset> ReadClockAsync(CancellationToken cancellationToken) =>
        dbContext.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"")
            .SingleAsync(cancellationToken);

    public async Task<ScanJobLease?> TryAcquireAsync(ScanRequestedMessage message, CancellationToken cancellationToken)
    {
        if (!access.IsSystem) throw new UnauthorizedAccessException("Lease/outbox maintenance is worker-only.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = await ReadClockAsync(cancellationToken);
        var token = Guid.NewGuid();
        var expiry = now.AddSeconds(options.Value.LeaseSeconds);
        var affected = await dbContext.Scans
            .Where(scan => scan.Id == message.ScanId
                && (scan.Status == ScanStatus.Pending
                    || (scan.Status == ScanStatus.Scanning && scan.LeaseToken == null)))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(scan => scan.Status, ScanStatus.Scanning)
                .SetProperty(scan => scan.LeaseToken, token)
                .SetProperty(scan => scan.LeaseExpiresAt, expiry)
                .SetProperty(scan => scan.LastHeartbeatAt, now)
                .SetProperty(scan => scan.StartedAt, scan => scan.StartedAt ?? now)
                .SetProperty(scan => scan.ExecutionAttempt, scan => scan.ExecutionAttempt + 1), cancellationToken);
        if (affected == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var snapshot = await dbContext.ScanOutboxMessages.SingleOrDefaultAsync(
            entry => entry.ScanId == message.ScanId, cancellationToken);
        if (snapshot is null)
        {
            // Capture an already queued pre-outbox request without guessing its selection/credentials.
            snapshot = new ScanOutboxMessage
            {
                ScanId = message.ScanId,
                Payload = JsonSerializer.Serialize(message, JsonOptions),
                PublishedAt = now,
                CreatedAt = now,
                AvailableAt = now
            };
            dbContext.ScanOutboxMessages.Add(snapshot);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var request = JsonSerializer.Deserialize<ScanRequestedMessage>(snapshot.Payload, JsonOptions)
            ?? throw new InvalidOperationException("Durable scan request is invalid.");
        var attempt = await dbContext.Scans.Where(scan => scan.Id == message.ScanId)
            .Select(scan => scan.ExecutionAttempt).SingleAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ScanJobLease(message.ScanId, token, attempt, request);
    }

    public async Task<bool> RenewAsync(ScanJobLease lease, CancellationToken cancellationToken)
    {
        if (!access.IsSystem) throw new UnauthorizedAccessException("Lease/outbox maintenance is worker-only.");
        var now = await ReadClockAsync(cancellationToken);
        var expiry = now.AddSeconds(options.Value.LeaseSeconds);
        return await dbContext.Scans.Where(scan => scan.Id == lease.ScanId
                && scan.Status == ScanStatus.Scanning && scan.LeaseToken == lease.Token
                && scan.LeaseExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(scan => scan.LastHeartbeatAt, now)
                .SetProperty(scan => scan.LeaseExpiresAt, expiry), cancellationToken) == 1;
    }

    public async Task<bool> RetryAsync(ScanJobLease lease, CancellationToken cancellationToken)
    {
        if (!access.IsSystem) throw new UnauthorizedAccessException("Lease/outbox maintenance is worker-only.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = await ReadClockAsync(cancellationToken);
        var rows = await dbContext.Scans.FromSqlInterpolated($"""
            SELECT * FROM scans WHERE "Id" = {lease.ScanId}
            AND "Status" = 'Scanning' AND "LeaseToken" = {lease.Token}
            AND "LeaseExpiresAt" > {now} FOR UPDATE
            """).ToListAsync(cancellationToken);
        var scan = rows.SingleOrDefault();
        if (scan is null) return false;
        await RequeueLockedAsync(scan, now, "execution_interrupted", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<int> RecoverExpiredAsync(CancellationToken cancellationToken)
    {
        if (!access.IsSystem) throw new UnauthorizedAccessException("Lease/outbox maintenance is worker-only.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = await ReadClockAsync(cancellationToken);
        var scans = await dbContext.Scans.FromSqlInterpolated($"""
            SELECT * FROM scans WHERE "Status" = 'Scanning' AND "LeaseExpiresAt" <= {now}
            ORDER BY "LeaseExpiresAt" LIMIT 100 FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);
        foreach (var scan in scans)
            await RequeueLockedAsync(scan, now, "execution_lease_expired", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return scans.Count;
    }

    private async Task RequeueLockedAsync(Scan scan, DateTimeOffset now, string errorCode, CancellationToken cancellationToken)
    {
        var snapshot = await dbContext.ScanOutboxMessages.SingleOrDefaultAsync(
            entry => entry.ScanId == scan.Id, cancellationToken);
        var exhausted = scan.ExecutionAttempt >= options.Value.MaxExecutionAttempts;
        scan.LeaseToken = null;
        scan.LeaseExpiresAt = null;
        scan.Status = exhausted || snapshot is null ? ScanStatus.Failed : ScanStatus.Pending;
        scan.ErrorMessage = snapshot is null ? "recovery_snapshot_missing"
            : exhausted ? "execution_attempts_exhausted" : errorCode;
        scan.FinishedAt = scan.Status == ScanStatus.Failed ? now : null;
        if (scan.Status == ScanStatus.Pending) scan.RetryCount++;
        if (snapshot is not null)
        {
            snapshot.PublishedAt = null;
            snapshot.DeadLetter = exhausted;
            snapshot.DispatchToken = null;
            snapshot.DispatchExpiresAt = null;
            snapshot.AvailableAt = now.AddSeconds(options.Value.RetryDelaySeconds);
            snapshot.LastErrorCode = scan.ErrorMessage;
        }
    }

    public async Task<ClaimedScanOutbox?> ClaimOutboxAsync(CancellationToken cancellationToken)
    {
        if (!access.IsSystem) throw new UnauthorizedAccessException("Lease/outbox maintenance is worker-only.");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = await ReadClockAsync(cancellationToken);
        var rows = await dbContext.ScanOutboxMessages.FromSqlInterpolated($"""
            SELECT o.* FROM scan_outbox_messages o JOIN scans s ON s."Id" = o."ScanId"
            WHERE o."PublishedAt" IS NULL AND o."AvailableAt" <= {now}
            AND (o."DispatchExpiresAt" IS NULL OR o."DispatchExpiresAt" <= {now})
            AND (o."DeadLetter" OR s."Status" = 'Pending')
            ORDER BY o."AvailableAt" LIMIT 1 FOR UPDATE OF o SKIP LOCKED
            """).ToListAsync(cancellationToken);
        var entry = rows.SingleOrDefault();
        if (entry is null) return null;
        var token = Guid.NewGuid();
        entry.DispatchToken = token;
        entry.DispatchExpiresAt = now.AddSeconds(options.Value.DispatchLeaseSeconds);
        entry.DeliveryAttempts++;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ClaimedScanOutbox(entry.Id, entry.ScanId, token, entry.Payload, entry.DeadLetter);
    }

    public async Task MarkPublishedAsync(ClaimedScanOutbox entry, CancellationToken cancellationToken)
    {
        if (!access.IsSystem) throw new UnauthorizedAccessException("Lease/outbox maintenance is worker-only.");
        var now = await ReadClockAsync(cancellationToken);
        await dbContext.ScanOutboxMessages.Where(row => row.Id == entry.Id && row.DispatchToken == entry.Token)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.PublishedAt, now)
                .SetProperty(row => row.DispatchToken, (Guid?)null)
                .SetProperty(row => row.DispatchExpiresAt, (DateTimeOffset?)null)
                .SetProperty(row => row.LastErrorCode, (string?)null), cancellationToken);
    }

    public async Task ReschedulePublishAsync(ClaimedScanOutbox entry, CancellationToken cancellationToken)
    {
        if (!access.IsSystem) throw new UnauthorizedAccessException("Lease/outbox maintenance is worker-only.");
        var next = (await ReadClockAsync(cancellationToken)).AddSeconds(options.Value.RetryDelaySeconds);
        await dbContext.ScanOutboxMessages.Where(row => row.Id == entry.Id && row.DispatchToken == entry.Token)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.AvailableAt, next)
                .SetProperty(row => row.DispatchToken, (Guid?)null)
                .SetProperty(row => row.DispatchExpiresAt, (DateTimeOffset?)null)
                .SetProperty(row => row.LastErrorCode, "broker_publish_failed"), cancellationToken);
    }
}
