using DbScanner.Application.Models;
using DbScanner.Domain.Enums;

namespace DbScanner.Infrastructure.Scanning;

internal static class ScanExecutorStrategyResults
{
    internal static ScanExecutorResult FromLegacyResults(
        string executorType,
        IReadOnlyCollection<ScanScriptExecutionResult> results)
    {
        var checkResults = results
            .Select(result => result.ToCheckExecutionResult(executorType))
            .ToArray();
        var structuredOutput = string.Join(
            "; ",
            results.Select(result => $"{result.ScriptName}={result.NormalizedOutput}"));

        return new ScanExecutorResult(structuredOutput, checkResults, []);
    }

    internal static void ValidateRequest(
        string executorType,
        ScanExecutionContext context,
        IReadOnlyCollection<ScanCheckDefinition> checks)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(checks);

        var foreignCheck = checks.FirstOrDefault(check => !string.Equals(
            check.ExecutorType,
            executorType,
            StringComparison.Ordinal));
        if (foreignCheck is not null)
        {
            throw new InvalidOperationException(
                $"Executor '{executorType}' cannot run check '{foreignCheck.Id}' assigned to " +
                $"'{foreignCheck.ExecutorType}'.");
        }
    }

    internal static void EnsureTargetEngine(
        string executorType,
        ScanExecutionContext context,
        DatabaseEngine expectedEngine)
    {
        if (context.Target.Engine != expectedEngine)
        {
            throw new InvalidOperationException(
                $"Executor '{executorType}' cannot run against " +
                $"'{context.Target.Engine.ToApiString()}' targets.");
        }
    }
}
