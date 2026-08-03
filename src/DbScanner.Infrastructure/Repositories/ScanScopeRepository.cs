using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using DbScanner.Infrastructure.Discovery;

namespace DbScanner.Infrastructure.Repositories;

public sealed class ScanScopeRepository(DbScannerDbContext dbContext) : IScanScopeRepository
{
    public async Task<IReadOnlyList<ScanScopeAllowlist>> ListEnabledAsync(CancellationToken cancellationToken = default) =>
        await dbContext.ScanScopeAllowlist
            .AsNoTracking()
            .Where(scope => scope.Enabled)
            .OrderBy(scope => scope.ScopeValue)
            .ToListAsync(cancellationToken);

    public async Task<bool> IsAllowedAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        var allowlist = await ListEnabledAsync(cancellationToken);
        return NetworkScope.IsHostAllowed(host, port, allowlist);
    }
}
