using DbScanner.Application.Models;
using DbScanner.Contracts.Scans;

namespace DbScanner.Application.Ports;

public interface IScanJobLeaseStore
{
    Task<ScanJobLease?> TryAcquireAsync(ScanRequestedMessage message, CancellationToken cancellationToken);
    Task<bool> RenewAsync(ScanJobLease lease, CancellationToken cancellationToken);
    Task<bool> RetryAsync(ScanJobLease lease, CancellationToken cancellationToken);
    Task<int> RecoverExpiredAsync(CancellationToken cancellationToken);
}
