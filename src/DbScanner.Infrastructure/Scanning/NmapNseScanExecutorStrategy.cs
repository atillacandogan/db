using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Enums;

namespace DbScanner.Infrastructure.Scanning;

internal sealed class NmapNseScanExecutorStrategy(
    ScriptCompatibilityScannerRuntime scannerRuntime) : IScanExecutorStrategy
{
    public string ExecutorType => ScanExecutorTypes.NmapNse;

    public ScanExecutorCapability Capability => new(
        ExecutorType,
        "Nmap NSE",
        "Executes installed Nmap Scripting Engine checks.",
        Enum.GetValues<DatabaseEngine>(),
        AllowsManualCheckCreation: true,
        AllowsCheckNameEditing: false,
        SupportsConfigurationSchema: true);

    public async Task<ScanExecutorResult> ExecuteAsync(
        ScanExecutionContext context,
        IReadOnlyCollection<ScanCheckDefinition> checks,
        CancellationToken cancellationToken = default)
    {
        ScanExecutorStrategyResults.ValidateRequest(ExecutorType, context, checks);
        var scripts = checks.Select(check => check.ToNseScript()).ToArray();
        var result = await scannerRuntime.RunAsync(
            context.Scan,
            context.Target,
            scripts,
            context.Authentication,
            cancellationToken);

        if (context.Target.Engine == DatabaseEngine.MsSql)
        {
            result = MsSqlNmapResultNormalizer.NormalizeMsSqlInfoResult(result, scripts);
        }

        var genericResult = result.ToCompositeScanExecutionResult(ExecutorType);
        return new ScanExecutorResult(
            genericResult.StructuredOutput,
            genericResult.CheckResults,
            genericResult.Artifacts);
    }
}
