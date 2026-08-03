using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Application.Ports;

public interface IRuleRepository
{
    Task<IReadOnlyList<Rule>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Rule>> ListEnabledByEngineAsync(DatabaseEngine engine, CancellationToken cancellationToken = default);
    Task<Rule?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task AddAsync(Rule rule, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
