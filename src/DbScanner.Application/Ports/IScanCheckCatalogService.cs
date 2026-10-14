using DbScanner.Application.Models;
using DbScanner.Contracts.ScanChecks;

namespace DbScanner.Application.Ports;

public interface IScanCheckCatalogService
{
    Task<IReadOnlyList<ScanCheckDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<ScanCheckDto> CreateAsync(CreateScanCheckCommand command, CancellationToken cancellationToken = default);
    Task<ScanCheckDto> UpdateAsync(string id, UpdateScanCheckCommand command, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
