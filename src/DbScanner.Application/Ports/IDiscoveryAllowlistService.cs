using DbScanner.Application.Models;
using DbScanner.Contracts.Discovery;

namespace DbScanner.Application.Ports;

public interface IDiscoveryAllowlistService
{
    Task<IReadOnlyList<DiscoveryAllowlistEntryDto>> ListAsync(
        string? environment = null,
        CancellationToken cancellationToken = default);
    Task<DiscoveryAllowlistEntryDto> CreateAsync(
        UpsertDiscoveryAllowlistCommand command,
        CancellationToken cancellationToken = default);
    Task<DiscoveryAllowlistEntryDto> CreateOrMergeAsync(
        UpsertDiscoveryAllowlistCommand command,
        CancellationToken cancellationToken = default);
    Task<DiscoveryAllowlistEntryDto> UpdateAsync(
        Guid id,
        UpsertDiscoveryAllowlistCommand command,
        CancellationToken cancellationToken = default);
    Task<DiscoveryAllowlistEntryDto> SetActiveAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
