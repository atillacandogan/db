using DbScanner.Application.Ports;
using DbScanner.Application.Models;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DbScanner.Infrastructure.Repositories;

public sealed class ScanRepository(DbScannerDbContext dbContext, ScanExecutionLeaseContext? leaseContext = null) : IScanRepository
{
    private static readonly ScanStatus[] ActiveStatuses = [ScanStatus.Pending, ScanStatus.Scanning];

    public Task<Scan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Scans
            .Include(scan => scan.Target)
            .Include(scan => scan.ScriptResults)
            .Include(scan => scan.Findings)
            .FirstOrDefaultAsync(scan => scan.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Scan>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Scans
            .AsNoTracking()
            .Include(scan => scan.Target)
            .OrderByDescending(scan => scan.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Scan>> ListByTargetAsync(Guid targetId, CancellationToken cancellationToken = default) =>
        await dbContext.Scans
            .AsNoTracking()
            .Include(scan => scan.Target)
            .Where(scan => scan.TargetId == targetId)
            .OrderByDescending(scan => scan.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<bool> HasActiveScanAsync(Guid targetId, CancellationToken cancellationToken = default) =>
        dbContext.Scans.AnyAsync(
            scan => scan.TargetId == targetId && ActiveStatuses.Contains(scan.Status),
            cancellationToken);

    public async Task AddAsync(Scan scan, CancellationToken cancellationToken = default) =>
        await dbContext.Scans.AddAsync(scan, cancellationToken);

    public async Task DeleteAsync(Scan scan, CancellationToken cancellationToken = default)
    {
        await dbContext.Scans
            .Where(item => item.Id == scan.Id && !ActiveStatuses.Contains(item.Status))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<bool> TryMarkScanningAsync(Guid scanId, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;

        var affectedRows = await dbContext.Scans
            .Where(scan => scan.Id == scanId && scan.Status == ScanStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(scan => scan.Status, ScanStatus.Scanning)
                .SetProperty(scan => scan.StartedAt, scan => scan.StartedAt ?? startedAt),
                cancellationToken);

        return affectedRows == 1;
    }

    public async Task IncrementRetryCountAsync(Guid scanId, CancellationToken cancellationToken = default)
    {
        await dbContext.Scans
            .Where(scan => scan.Id == scanId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(scan => scan.RetryCount, scan => scan.RetryCount + 1),
                cancellationToken);
    }

    public async Task StoreExecutionArtifactsAsync(
        Guid scanId,
        IReadOnlyCollection<ScanScriptResult> scriptResults,
        IReadOnlyCollection<Finding> findings,
        int? riskScore,
        Severity? overallSeverity,
        ScanStatus status,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        // Lock/fence the scan before deleting or inserting evidence. A cancelled or
        // superseded execution must never replace another attempt's results.
        await UpdateStatusAsync(scanId, status, errorMessage, cancellationToken);

        await dbContext.ScanScriptResults
            .Where(result => result.ScanId == scanId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.Findings
            .Where(finding => finding.ScanId == scanId)
            .ExecuteDeleteAsync(cancellationToken);

        if (scriptResults.Count > 0)
        {
            await dbContext.ScanScriptResults.AddRangeAsync(scriptResults, cancellationToken);
        }

        if (findings.Count > 0)
        {
            await dbContext.Findings.AddRangeAsync(findings, cancellationToken);
        }

        var finishedAt = DateTimeOffset.UtcNow;
        var affectedRows = await dbContext.Scans
            .Where(scan => scan.Id == scanId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(scan => scan.RiskScore, riskScore)
                .SetProperty(scan => scan.OverallSeverity, overallSeverity)
                .SetProperty(scan => scan.Status, status)
                .SetProperty(scan => scan.ErrorMessage, errorMessage)
                .SetProperty(scan => scan.FinishedAt, finishedAt),
                cancellationToken);

        if (affectedRows == 0)
        {
            throw new InvalidOperationException($"Scan {scanId} was not found while storing execution artifacts.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(
        Guid scanId,
        ScanStatus status,
        string? errorMessage = null,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var finishedAt = DateTimeOffset.UtcNow;
        var shouldSetFinishedAt =
            status == ScanStatus.Scanned ||
            status == ScanStatus.Failed ||
            status == ScanStatus.Timeout ||
            status == ScanStatus.Cancelled;

        var query = dbContext.Scans.Where(scan => scan.Id == scanId);
        if (status == ScanStatus.Cancelled && leaseContext?.Token is null)
            query = query.Where(scan => scan.Status == ScanStatus.Pending || scan.Status == ScanStatus.Scanning);
        if (leaseContext?.Token is Guid token)
        {
            if (leaseContext.ScanId != scanId) throw new ScanLeaseLostException(scanId);
            var now = await dbContext.Database.SqlQuery<DateTimeOffset>($"SELECT clock_timestamp() AS \"Value\"")
                .SingleAsync(cancellationToken);
            query = query.Where(scan => scan.Status == ScanStatus.Scanning
                && scan.LeaseToken == token && scan.LeaseExpiresAt > now);
        }

        var affected = await query
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(scan => scan.Status, status)
                .SetProperty(scan => scan.ErrorMessage, errorMessage)
                .SetProperty(scan => scan.LeaseToken, scan => shouldSetFinishedAt ? null : scan.LeaseToken)
                .SetProperty(scan => scan.LeaseExpiresAt, scan => shouldSetFinishedAt ? null : scan.LeaseExpiresAt)
                .SetProperty(
                    scan => scan.StartedAt,
                    scan => status == ScanStatus.Scanning ? scan.StartedAt ?? startedAt : scan.StartedAt)
                .SetProperty(
                    scan => scan.FinishedAt,
                    scan => shouldSetFinishedAt
                        ? scan.FinishedAt ?? finishedAt
                        : scan.FinishedAt),
                cancellationToken);

        if (leaseContext?.Token is not null && affected == 0)
            throw new ScanLeaseLostException(scanId);
    }

    public void ClearTracking() =>
        dbContext.ChangeTracker.Clear();

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
