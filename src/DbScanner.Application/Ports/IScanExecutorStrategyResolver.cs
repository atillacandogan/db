using DbScanner.Application.Models;

namespace DbScanner.Application.Ports;

public interface IScanExecutorStrategyResolver
{
    IReadOnlyCollection<string> RegisteredExecutorTypes { get; }
    IReadOnlyCollection<ScanExecutorCapability> RegisteredCapabilities { get; }

    IScanExecutorStrategy Resolve(string executorType);
}
