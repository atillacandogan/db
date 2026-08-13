using DbScanner.Contracts.Discovery;

namespace DbScanner.Application.Models;

public sealed record DiscoveryScopePlan(
    IReadOnlyList<DiscoveryEndpoint> Endpoints,
    IReadOnlyList<DiscoveryTargetValidationDto> Targets)
{
    public bool IsValid => Targets.Any(target =>
        target.Status == DiscoveryTargetValidationStatus.Authorized);
}
