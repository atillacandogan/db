using DbScanner.Application.Models;

namespace DbScanner.Application.Ports;

public interface IScanExecutionOrchestrator
{
    Task<CompositeScanExecutionResult> ExecuteAsync(
        ScanExecutionContext context,
        IReadOnlyCollection<ScanCheckDefinition> checks,
        CancellationToken cancellationToken = default);
}
