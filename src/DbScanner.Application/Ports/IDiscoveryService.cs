using DbScanner.Application.Models;
using DbScanner.Contracts.Discovery;

namespace DbScanner.Application.Ports;

public interface IDiscoveryService
{
    Task<IReadOnlyList<DiscoveryJobDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<DiscoveryJobDto?> GetAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<DiscoveryJobDto> CreateAsync(CreateDiscoveryCommand command, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DiscoveryResultDto>> AddTargetsAsync(Guid jobId, IReadOnlyCollection<Guid> resultIds, CancellationToken cancellationToken = default);
}
