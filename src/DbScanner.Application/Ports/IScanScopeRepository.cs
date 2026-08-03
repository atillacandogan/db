using DbScanner.Domain.Entities;

namespace DbScanner.Application.Ports;

public interface IScanScopeRepository
{
    Task<IReadOnlyList<ScanScopeAllowlist>> ListEnabledAsync(CancellationToken cancellationToken = default);
    Task<bool> IsAllowedAsync(string host, int port, CancellationToken cancellationToken = default);
}
