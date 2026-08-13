using DbScanner.Domain.Entities;

namespace DbScanner.Application.Ports;

public interface IScanScopeRepository
{
    Task<IReadOnlyList<ScanScopeAllowlist>> ListEnabledAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScanScopeAllowlist>> ListAsync(
        string? environment = null,
        CancellationToken cancellationToken = default);
    Task<ScanScopeAllowlist?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(ScanScopeAllowlist entry, CancellationToken cancellationToken = default);
    Task DeleteAsync(ScanScopeAllowlist entry, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<bool> IsAllowedAsync(
        string host,
        int port,
        string environment,
        DbScanner.Domain.Enums.DatabaseEngine engine,
        CancellationToken cancellationToken = default);
}
