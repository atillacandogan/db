using DbScanner.Application.Models;
using DbScanner.Domain.Entities;

namespace DbScanner.Application.Ports;

public interface IScannerRuntime
{
    Task<ScanExecutionResult> RunAsync(
        Scan scan,
        Target target,
        IReadOnlyCollection<NseScript> scripts,
        DatabaseAuthentication? authentication = null,
        CancellationToken cancellationToken = default);
}
