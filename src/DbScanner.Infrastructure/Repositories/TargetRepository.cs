using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DbScanner.Infrastructure.Repositories;

public sealed class TargetRepository(DbScannerDbContext dbContext) : ITargetRepository
{
    public async Task<IReadOnlyList<Target>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Targets
            .AsNoTracking()
            .Include(target => target.Scans)
            .OrderBy(target => target.Name)
            .ToListAsync(cancellationToken);

    public Task<Target?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Targets
            .Include(target => target.Scans)
            .FirstOrDefaultAsync(target => target.Id == id, cancellationToken);

    public async Task AddAsync(Target target, CancellationToken cancellationToken = default) =>
        await dbContext.Targets.AddAsync(target, cancellationToken);

    public Task DeleteAsync(Target target, CancellationToken cancellationToken = default)
    {
        dbContext.Targets.Remove(target);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
