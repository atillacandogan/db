using DbScanner.Contracts.Findings;

namespace DbScanner.Application.Ports;

public interface IFindingCatalogService
{
    Task<IReadOnlyList<FindingListItemDto>> ListLatestAsync(int limit = 50, CancellationToken cancellationToken = default);
}
