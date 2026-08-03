using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Application.Ports;

public interface IScanRepository
{
    Task<Scan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Scan>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Scan>> ListByTargetAsync(Guid targetId, CancellationToken cancellationToken = default);
    Task<bool> HasActiveScanAsync(Guid targetId, CancellationToken cancellationToken = default);
    Task AddAsync(Scan scan, CancellationToken cancellationToken = default);
    Task DeleteAsync(Scan scan, CancellationToken cancellationToken = default);
    Task<bool> TryMarkScanningAsync(Guid scanId, CancellationToken cancellationToken = default);
    Task IncrementRetryCountAsync(Guid scanId, CancellationToken cancellationToken = default);
    Task StoreExecutionArtifactsAsync(
        Guid scanId,
        IReadOnlyCollection<ScanScriptResult> scriptResults,
        IReadOnlyCollection<Finding> findings,
        int? riskScore,
        Severity? overallSeverity,
        ScanStatus status,
        string? errorMessage = null,
        CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(Guid scanId, ScanStatus status, string? errorMessage = null, CancellationToken cancellationToken = default);
    void ClearTracking();
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
