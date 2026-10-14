using DbScanner.Application.Models;

namespace DbScanner.Application.Ports;

public interface IScanExecutorStrategy
{
    string ExecutorType { get; }

    ScanExecutorCapability Capability =>
        ScanExecutorCapability.ConservativeDefault(ExecutorType);

    Task<ScanExecutorResult> ExecuteAsync(
        ScanExecutionContext context,
        IReadOnlyCollection<ScanCheckDefinition> checks,
        CancellationToken cancellationToken = default);
}
