using DbScanner.Application.Models;
using DbScanner.Domain.Enums;

namespace DbScanner.Application.Ports;

public interface IDiscoveryScopeValidator
{
    Task<IReadOnlyList<DiscoveryEndpoint>> PlanAuthorizedAsync(
        string environment,
        IReadOnlySet<DatabaseEngine> supportedEngineFilter,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DiscoveryEndpoint>> PlanLocalNetworksAsync(
        IReadOnlyCollection<string> cidrs,
        IReadOnlySet<DatabaseEngine> supportedEngineFilter,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DiscoveryEndpoint>> ValidateAndPlanAsync(
        string scopeInput,
        string environment,
        IReadOnlySet<DatabaseEngine> supportedEngineFilter,
        CancellationToken cancellationToken = default);

    Task<DiscoveryScopePlan> ValidateAndPlanDetailedAsync(
        string scopeInput,
        string environment,
        IReadOnlySet<DatabaseEngine> supportedEngineFilter,
        CancellationToken cancellationToken = default);
}
