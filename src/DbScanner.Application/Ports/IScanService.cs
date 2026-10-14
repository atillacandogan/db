using DbScanner.Domain.Entities;

namespace DbScanner.Application.Ports;

public interface IScanService
{
    Task<Scan> CreateScanAsync(Guid targetId, CancellationToken cancellationToken = default);

    Task<Scan> CreateScanAsync(
        Guid targetId,
        IReadOnlyCollection<string>? selectedCheckIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Scan>> ListScansAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Scan>> ListScansByTargetAsync(Guid targetId, CancellationToken cancellationToken = default);
    Task<Scan?> GetScanAsync(Guid scanId, CancellationToken cancellationToken = default);
    Task<bool> CancelScanAsync(Guid scanId, CancellationToken cancellationToken = default);
    Task<bool> DeleteScanAsync(Guid scanId, CancellationToken cancellationToken = default);
}
