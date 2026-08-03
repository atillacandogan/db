using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DbScanner.Infrastructure.Repositories;

public sealed class RuleRepository(DbScannerDbContext dbContext) : IRuleRepository
{
    public async Task<IReadOnlyList<Rule>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Rules
            .AsNoTracking()
            .Include(rule => rule.EngineSupport)
            .Include(rule => rule.RuleScripts)
            .OrderBy(rule => rule.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Rule>> ListEnabledByEngineAsync(DatabaseEngine engine, CancellationToken cancellationToken = default) =>
        await dbContext.Rules
            .AsNoTracking()
            .Include(rule => rule.RuleScripts)
            .Where(rule => rule.Enabled && (rule.Engine == DatabaseEngine.Generic || rule.Engine == engine))
            .Where(rule => rule.EngineSupport.Any(support => support.Engine == engine))
            .OrderBy(rule => rule.Id)
            .ToListAsync(cancellationToken);

    public Task<Rule?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        dbContext.Rules
            .Include(rule => rule.EngineSupport)
            .Include(rule => rule.RuleScripts)
            .FirstOrDefaultAsync(rule => rule.Id == id, cancellationToken);

    public Task AddAsync(Rule rule, CancellationToken cancellationToken = default) =>
        dbContext.Rules.AddAsync(rule, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
