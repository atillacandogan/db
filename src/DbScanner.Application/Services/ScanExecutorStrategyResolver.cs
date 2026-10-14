using DbScanner.Application.Models;
using DbScanner.Application.Ports;

namespace DbScanner.Application.Services;

public sealed class ScanExecutorStrategyResolver : IScanExecutorStrategyResolver
{
    private readonly IReadOnlyDictionary<string, IScanExecutorStrategy> strategies;

    public ScanExecutorStrategyResolver(IEnumerable<IScanExecutorStrategy> strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);

        var registrations = new Dictionary<string, IScanExecutorStrategy>(StringComparer.Ordinal);
        foreach (var strategy in strategies)
        {
            if (!ScanExecutorTypes.IsValid(strategy.ExecutorType))
            {
                throw new InvalidOperationException(
                    $"Scan executor strategy '{strategy.GetType().Name}' has invalid executor type " +
                    $"'{strategy.ExecutorType}'.");
            }

            if (!string.Equals(
                    strategy.Capability.ExecutorType,
                    strategy.ExecutorType,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Scan executor strategy '{strategy.GetType().Name}' capability type " +
                    $"'{strategy.Capability.ExecutorType}' does not match '{strategy.ExecutorType}'.");
            }

            if (!registrations.TryAdd(strategy.ExecutorType, strategy))
            {
                throw new InvalidOperationException(
                    $"Multiple scan executor strategies are registered for '{strategy.ExecutorType}'.");
            }
        }

        this.strategies = registrations;
        RegisteredExecutorTypes = registrations.Keys.Order(StringComparer.Ordinal).ToArray();
        RegisteredCapabilities = registrations.Values
            .Select(strategy => strategy.Capability)
            .OrderBy(capability => capability.ExecutorType, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyCollection<string> RegisteredExecutorTypes { get; }
    public IReadOnlyCollection<ScanExecutorCapability> RegisteredCapabilities { get; }

    public IScanExecutorStrategy Resolve(string executorType)
    {
        if (!ScanExecutorTypes.IsValid(executorType))
        {
            throw new ArgumentException(
                $"Invalid scan executor type '{executorType}'.",
                nameof(executorType));
        }

        return strategies.TryGetValue(executorType, out var strategy)
            ? strategy
            : throw new InvalidOperationException(
                $"No scan executor strategy is registered for '{executorType}'.");
    }
}
