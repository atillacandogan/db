using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using DbScanner.Infrastructure.Discovery;
using DbScanner.Domain.Enums;

namespace DbScanner.Infrastructure.Repositories;

public sealed class ScanScopeRepository(DbScannerDbContext dbContext) : IScanScopeRepository
{
    public async Task<IReadOnlyList<ScanScopeAllowlist>> ListEnabledAsync(CancellationToken cancellationToken = default) =>
        await dbContext.ScanScopeAllowlist
            .AsNoTracking()
            .Where(scope => scope.Enabled)
            .OrderBy(scope => scope.ScopeValue)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ScanScopeAllowlist>> ListAsync(
        string? environment = null,
        CancellationToken cancellationToken = default) =>
        await dbContext.ScanScopeAllowlist
            .AsNoTracking()
            .Where(scope => environment == null || scope.EnvironmentTag == environment)
            .OrderBy(scope => scope.EnvironmentTag)
            .ThenBy(scope => scope.Name)
            .ThenBy(scope => scope.ScopeValue)
            .ToListAsync(cancellationToken);

    public Task<ScanScopeAllowlist?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.ScanScopeAllowlist.FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken);

    public async Task AddAsync(ScanScopeAllowlist entry, CancellationToken cancellationToken = default) =>
        await dbContext.ScanScopeAllowlist.AddAsync(entry, cancellationToken);

    public Task DeleteAsync(ScanScopeAllowlist entry, CancellationToken cancellationToken = default)
    {
        dbContext.ScanScopeAllowlist.Remove(entry);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public async Task<bool> IsAllowedAsync(
        string host,
        int port,
        string environment,
        DatabaseEngine engine,
        CancellationToken cancellationToken = default)
    {
        var allowlist = await dbContext.ScanScopeAllowlist
            .AsNoTracking()
            .Where(scope => scope.Enabled && scope.EnvironmentTag == environment)
            .ToListAsync(cancellationToken);
        return NetworkScope.IsHostAllowed(host, port, engine, allowlist);
    }
}
