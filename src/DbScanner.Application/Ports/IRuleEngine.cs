using DbScanner.Application.Models;
using DbScanner.Domain.Entities;

namespace DbScanner.Application.Ports;

public interface IRuleEngine
{
    Task<IReadOnlyList<Finding>> EvaluateAsync(
        Scan scan,
        Target target,
        IReadOnlyCollection<Rule> rules,
        ScanExecutionResult executionResult,
        CancellationToken cancellationToken = default);
}
