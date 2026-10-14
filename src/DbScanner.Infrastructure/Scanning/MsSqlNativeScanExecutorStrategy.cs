using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Infrastructure.Scanning;

internal sealed class MsSqlNativeScanExecutorStrategy(
    IMsSqlAuthenticatedProbe authenticatedProbe,
    IMsSqlBrowserProbe browserProbe) : IScanExecutorStrategy
{
    public string ExecutorType => ScanExecutorTypes.MsSqlNative;

    public ScanExecutorCapability Capability => new(
        ExecutorType,
        "Microsoft SQL Server Native",
        "Runs the built-in SQL Server Browser and authenticated probes.",
        [DatabaseEngine.MsSql],
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
            DatabaseEngine.MsSql);

        var scripts = checks.Select(check => check.ToLegacyNseScript()).ToArray();
        var unsupported = scripts.FirstOrDefault(script =>
            !string.Equals(
                script.NseScriptName,
                MsSqlProbeScripts.BrowserDiscovery,
                StringComparison.OrdinalIgnoreCase)
            && !MsSqlProbeScripts.IsAuthenticatedProbe(script.NseScriptName));
        if (unsupported is not null)
        {
            throw new InvalidOperationException(
                $"Microsoft SQL Server native executor does not support check " +
                $"'{unsupported.NseScriptName}'.");
        }

        var browserScripts = scripts.Where(script => string.Equals(
            script.NseScriptName,
            MsSqlProbeScripts.BrowserDiscovery,
            StringComparison.OrdinalIgnoreCase)).ToArray();
        if (browserScripts.Length > 1)
        {
            throw new InvalidOperationException(
                "Microsoft SQL Server Browser discovery can be requested only once per scan.");
        }

        var results = new List<ScanScriptExecutionResult>();
        if (browserScripts.Length == 1)
        {
            results.Add(await browserProbe.RunAsync(
                context.Target,
                browserScripts[0],
                cancellationToken));
        }

        var authenticatedScripts = scripts
            .Where(script => MsSqlProbeScripts.IsAuthenticatedProbe(script.NseScriptName))
            .ToArray();
        if (authenticatedScripts.Length > 0)
        {
            if (context.Authentication is null)
            {
                results.AddRange(authenticatedScripts.Select(CreateCredentialsMissingResult));
            }
            else
            {
                results.AddRange(await authenticatedProbe.RunAsync(
                    context.Target,
                    authenticatedScripts,
                    context.Authentication,
                    cancellationToken));
            }
        }

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
