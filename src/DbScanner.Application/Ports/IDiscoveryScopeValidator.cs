using DbScanner.Application.Models;
using DbScanner.Domain.Enums;

namespace DbScanner.Application.Ports;

public interface IDiscoveryScopeValidator
{
    Task<IReadOnlyList<DiscoveryEndpoint>> ValidateAndPlanAsync(
        string scopeInput,
        IReadOnlySet<DatabaseEngine> supportedEngineFilter,
        CancellationToken cancellationToken = default);
}
