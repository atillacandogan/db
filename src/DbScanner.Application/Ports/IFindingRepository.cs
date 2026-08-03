using DbScanner.Domain.Entities;

namespace DbScanner.Application.Ports;

public interface IFindingRepository
{
    Task<IReadOnlyList<Finding>> ListLatestAsync(int limit = 50, CancellationToken cancellationToken = default);
}
