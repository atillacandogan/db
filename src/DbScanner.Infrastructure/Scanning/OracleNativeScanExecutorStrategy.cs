using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Infrastructure.Scanning;

internal sealed class OracleNativeScanExecutorStrategy(
    IOracleAuthenticatedProbe authenticatedProbe) : IScanExecutorStrategy
{
    public string ExecutorType => ScanExecutorTypes.OracleNative;

    public ScanExecutorCapability Capability => new(
        ExecutorType,
        "Oracle Native",
        "Runs the built-in authenticated Oracle probes.",
        [DatabaseEngine.Oracle],
        AllowsManualCheckCreation: false,
        AllowsCheckNameEditing: false,
        SupportsConfigurationSchema: false);

    public async Task<ScanExecutorResult> ExecuteAsync(
        ScanExecutionContext context,
        IReadOnlyCollection<ScanCheckDefinition> checks,
        CancellationToken cancellationToken = default)
    {
        ScanExecutorStrategyResults.ValidateRequest(ExecutorType, context, checks);
        ScanExecutorStrategyResults.EnsureTargetEngine(
            ExecutorType,
            context,
            DatabaseEngine.Oracle);

        var scripts = checks.Select(check => check.ToLegacyNseScript()).ToArray();
        var unsupported = scripts.FirstOrDefault(script =>
            !OracleProbeScripts.IsAuthenticatedProbe(script.NseScriptName));
        if (unsupported is not null)
        {
            throw new InvalidOperationException(
                $"Oracle native executor does not support check '{unsupported.NseScriptName}'.");
        }

        IReadOnlyCollection<ScanScriptExecutionResult> results = context.Authentication is null
            ? scripts.Select(CreateCredentialsMissingResult).ToArray()
            : await authenticatedProbe.RunAsync(
                context.Target,
                scripts,
                context.Authentication,
                cancellationToken);

        return ScanExecutorStrategyResults.FromLegacyResults(ExecutorType, results);
    }

    private static ScanScriptExecutionResult CreateCredentialsMissingResult(NseScript script) =>
        new(
            script.Id,
            script.NseScriptName,
            null,
            "status=skipped; reason=credentials_not_provided",
            0,
            ProbeResultStatus.Skipped);
}
