using DbScanner.Application.Exceptions;
using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

    public Task<Target?> FindByEndpointAsync(
        string normalizedHost,
        int port,
        DatabaseEngine engine,
        Guid? excludeTargetId = null,
        CancellationToken cancellationToken = default) =>
        dbContext.Targets
            .AsNoTracking()
            .FirstOrDefaultAsync(
                target => target.NormalizedHost == normalizedHost
                    && target.Port == port
                    && target.Engine == engine
                    && (!excludeTargetId.HasValue || target.Id != excludeTargetId.Value),
                cancellationToken);

    public async Task AddAsync(Target target, CancellationToken cancellationToken = default) =>
        await dbContext.Targets.AddAsync(target, cancellationToken);

    public Task DeleteAsync(Target target, CancellationToken cancellationToken = default)
    {
        dbContext.Targets.Remove(target);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "IX_targets_NormalizedHost_Port_Engine"
            })
        {
            var conflictingEntry = dbContext.ChangeTracker
                .Entries<Target>()
                .FirstOrDefault(entry => entry.State is EntityState.Added or EntityState.Modified);

            if (conflictingEntry is null)
            {
                throw;
            }

            var normalizedHost = conflictingEntry.Entity.NormalizedHost;
            var port = conflictingEntry.Entity.Port;
            var engine = conflictingEntry.Entity.Engine;
            var excludedId = conflictingEntry.State == EntityState.Modified
                ? conflictingEntry.Entity.Id
                : (Guid?)null;
            conflictingEntry.State = EntityState.Detached;

            var existingTargetId = await dbContext.Targets
                .AsNoTracking()
                .Where(target => target.NormalizedHost == normalizedHost
                    && target.Port == port
                    && target.Engine == engine
                    && (!excludedId.HasValue || target.Id != excludedId.Value))
                .Select(target => (Guid?)target.Id)
                .FirstOrDefaultAsync(cancellationToken);

            throw new TargetAlreadyExistsException(existingTargetId, exception);
        }
    }
}
