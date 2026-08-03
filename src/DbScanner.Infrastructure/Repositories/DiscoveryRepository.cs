using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DbScanner.Infrastructure.Repositories;

internal sealed class DiscoveryRepository(DbScannerDbContext dbContext) : IDiscoveryRepository
{
    public async Task<IReadOnlyList<DiscoveryJob>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.DiscoveryJobs
            .AsNoTracking()
            .Include(job => job.Results)
            .OrderByDescending(job => job.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<DiscoveryJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.DiscoveryJobs
            .Include(job => job.Results)
            .FirstOrDefaultAsync(job => job.Id == id, cancellationToken);

    public Task<DiscoveredDatabase?> GetResultAsync(
        Guid jobId,
        Guid resultId,
        CancellationToken cancellationToken = default) =>
        dbContext.DiscoveredDatabases.FirstOrDefaultAsync(
            result => result.DiscoveryJobId == jobId && result.Id == resultId,
            cancellationToken);

    public async Task AddAsync(DiscoveryJob job, CancellationToken cancellationToken = default) =>
        await dbContext.DiscoveryJobs.AddAsync(job, cancellationToken);

    public async Task<bool> TryMarkRunningAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var updated = await dbContext.DiscoveryJobs
            .Where(job => job.Id == jobId && job.Status == DiscoveryStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.Status, DiscoveryStatus.Running)
                    .SetProperty(job => job.StartedAt, DateTimeOffset.UtcNow)
                    .SetProperty(job => job.ErrorMessage, (string?)null),
                cancellationToken);
        return updated == 1;
    }

    public async Task CompleteAsync(
        Guid jobId,
        IReadOnlyCollection<DiscoveredDatabase> results,
        CancellationToken cancellationToken = default)
    {
        var job = await dbContext.DiscoveryJobs.FirstOrDefaultAsync(candidate => candidate.Id == jobId, cancellationToken)
            ?? throw new InvalidOperationException("Discovery job was not found.");
        await dbContext.DiscoveredDatabases.AddRangeAsync(results, cancellationToken);
        job.Status = DiscoveryStatus.Completed;
        job.FinishedAt = DateTimeOffset.UtcNow;
        job.ErrorMessage = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(Guid jobId, string errorMessage, CancellationToken cancellationToken = default)
    {
        var job = await dbContext.DiscoveryJobs.FirstOrDefaultAsync(candidate => candidate.Id == jobId, cancellationToken);
        if (job is null)
        {
            return;
        }

        job.Status = DiscoveryStatus.Failed;
        job.ErrorMessage = errorMessage;
        job.FinishedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
