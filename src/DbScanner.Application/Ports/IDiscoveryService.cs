using DbScanner.Application.Models;
using DbScanner.Contracts.Discovery;
using DbScanner.Domain.Enums;

namespace DbScanner.Application.Ports;

public interface IDiscoveryService
{
    Task<IReadOnlyList<DiscoveryJobDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<DiscoveryJobDto?> GetAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid jobId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LocalNetworkDto>> ListLocalNetworksAsync(CancellationToken cancellationToken = default);
    Task<DiscoveryJobDto> CreateLocalNetworkAsync(
        string name,
        string environment,
        IReadOnlyCollection<DatabaseEngine> supportedEngines,
        IReadOnlyCollection<string>? networks,
        CancellationToken cancellationToken = default);
    Task<DiscoveryScopeValidationDto> ValidateScopeAsync(
        string scope,
        string environment,
        IReadOnlyCollection<DatabaseEngine> supportedEngines,
        CancellationToken cancellationToken = default);
    Task<DiscoveryJobDto> CreateAsync(CreateDiscoveryCommand command, CancellationToken cancellationToken = default);
    Task<AddDiscoveryTargetsResponseDto> AddTargetsAsync(
        Guid jobId,
        IReadOnlyCollection<Guid> resultIds,
        bool isBulk,
        CancellationToken cancellationToken = default);
    Task<ApproveDiscoveryAllowlistResponseDto> ApproveAllowlistAsync(
        Guid jobId,
        IReadOnlyCollection<Guid> resultIds,
        CancellationToken cancellationToken = default);
}
