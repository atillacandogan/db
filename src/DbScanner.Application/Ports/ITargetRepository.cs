using DbScanner.Domain.Entities;

namespace DbScanner.Application.Ports;

public interface ITargetRepository
{
    Task<IReadOnlyList<Target>> ListAsync(CancellationToken cancellationToken = default);
    Task<Target?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Target target, CancellationToken cancellationToken = default);
    Task DeleteAsync(Target target, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
