using DbScanner.Domain.Entities;

namespace DbScanner.Application.Ports;

public interface IDiscoveryRepository
{
    Task<IReadOnlyList<DiscoveryJob>> ListAsync(CancellationToken cancellationToken = default);
    Task<DiscoveryJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DiscoveredDatabase?> GetResultAsync(Guid jobId, Guid resultId, CancellationToken cancellationToken = default);
    Task AddAsync(DiscoveryJob job, CancellationToken cancellationToken = default);
    Task<bool> TryMarkRunningAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task CompleteAsync(Guid jobId, IReadOnlyCollection<DiscoveredDatabase> results, CancellationToken cancellationToken = default);
    Task FailAsync(Guid jobId, string errorMessage, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
