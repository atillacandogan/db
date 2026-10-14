using DbScanner.Application.Models;
using DbScanner.Application.Ports;

namespace DbScanner.Application.Services;

public sealed class ScanExecutionOrchestrator(
    IScanExecutorStrategyResolver strategyResolver) : IScanExecutionOrchestrator
{
    public async Task<CompositeScanExecutionResult> ExecuteAsync(
        ScanExecutionContext context,
        IReadOnlyCollection<ScanCheckDefinition> checks,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(checks);

        var invalidCheck = checks.FirstOrDefault(check => string.IsNullOrWhiteSpace(check.Id));
        if (invalidCheck is not null)
        {
            throw new InvalidOperationException("The scan execution plan contains a check without an id.");
        }

        var duplicateCheckId = checks
            .GroupBy(check => check.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateCheckId is not null)
        {
            throw new InvalidOperationException(
                $"The scan execution plan contains duplicate check id '{duplicateCheckId}'.");
        }

        var structuredOutputs = new List<string>();
        var checkResults = new List<CheckExecutionResult>();
        var artifacts = new List<ScanExecutionArtifact>();
        var groupedChecks = checks
            .GroupBy(check => check.ExecutorType, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyCollection<ScanCheckDefinition>)group.ToArray(),
                StringComparer.Ordinal);
        var requiredExecutorTypes = (context.RequiredExecutorTypes ?? [])
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var executionOrder = requiredExecutorTypes
            .Concat(groupedChecks.Keys.Where(executorType =>
                !requiredExecutorTypes.Contains(executorType, StringComparer.Ordinal)))
            .ToArray();

        // Required baseline executors run first. Remaining groups retain their
        // first-seen order. Sequential execution keeps database load and merged
        // output deterministic.
        foreach (var executorType in executionOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var strategy = strategyResolver.Resolve(executorType);
            var executorChecks = groupedChecks.TryGetValue(executorType, out var group)
                ? group
                : [];
            var result = await strategy.ExecuteAsync(
                context,
                executorChecks,
                cancellationToken);

            ValidateResultOwnership(strategy.ExecutorType, executorChecks, result);

            if (!string.IsNullOrWhiteSpace(result.StructuredOutput))
            {
                structuredOutputs.Add(result.StructuredOutput);
            }

            checkResults.AddRange(result.CheckResults);
            artifacts.AddRange(result.Artifacts);
        }

        return new CompositeScanExecutionResult(
            string.Join("; ", structuredOutputs),
            checkResults,
            artifacts);
    }

    private static void ValidateResultOwnership(
        string executorType,
        IEnumerable<ScanCheckDefinition> requestedChecks,
        ScanExecutorResult result)
    {
        if (result is null)
        {
            throw new InvalidOperationException(
                $"Scan executor '{executorType}' returned no result.");
        }

        if (result.CheckResults is null || result.Artifacts is null)
        {
            throw new InvalidOperationException(
                $"Scan executor '{executorType}' returned an incomplete result.");
        }

        var foreignCheckResult = result.CheckResults.FirstOrDefault(item =>
            !string.Equals(item.ExecutorType, executorType, StringComparison.Ordinal));
        if (foreignCheckResult is not null)
        {
            throw new InvalidOperationException(
                $"Scan executor '{executorType}' returned a check result owned by " +
                $"'{foreignCheckResult.ExecutorType}'.");
        }

        var requestedCheckIds = requestedChecks
            .Select(check => check.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unrequestedCheckResult = result.CheckResults.FirstOrDefault(item =>
            item.CheckId is not null && !requestedCheckIds.Contains(item.CheckId));
        if (unrequestedCheckResult is not null)
        {
            throw new InvalidOperationException(
                $"Scan executor '{executorType}' returned unrequested check id " +
                $"'{unrequestedCheckResult.CheckId}'.");
        }

        var foreignArtifact = result.Artifacts.FirstOrDefault(item =>
            !string.Equals(item.ExecutorType, executorType, StringComparison.Ordinal));
        if (foreignArtifact is not null)
        {
            throw new InvalidOperationException(
                $"Scan executor '{executorType}' returned an artifact owned by " +
                $"'{foreignArtifact.ExecutorType}'.");
        }
    }
}
