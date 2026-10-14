using DbScanner.Application.Models;

namespace DbScanner.Application.Ports;

public interface IScanCompatibilityReadinessService
{
    Task<ScanCompatibilityReadiness> GetAsync(CancellationToken cancellationToken = default);
}
