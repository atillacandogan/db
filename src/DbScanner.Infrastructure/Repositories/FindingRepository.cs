using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DbScanner.Infrastructure.Repositories;

public sealed class FindingRepository(DbScannerDbContext dbContext) : IFindingRepository
{
    public async Task<IReadOnlyList<Finding>> ListLatestAsync(int limit = 50, CancellationToken cancellationToken = default) =>
        await dbContext.Findings
            .AsNoTracking()
            .Include(finding => finding.Target)
            .Include(finding => finding.Scan)
            .OrderByDescending(finding => finding.DetectedAt)
            .ThenByDescending(finding => finding.ScoreContribution)
            .Take(limit)
            .ToListAsync(cancellationToken);
}
