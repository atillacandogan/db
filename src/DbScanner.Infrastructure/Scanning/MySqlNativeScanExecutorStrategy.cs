using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Enums;

namespace DbScanner.Infrastructure.Scanning;

internal sealed class MySqlNativeScanExecutorStrategy(
    NmapScannerRuntime scannerRuntime) : IScanExecutorStrategy
{
    public string ExecutorType => ScanExecutorTypes.MySqlNative;

    public ScanExecutorCapability Capability => new(
        ExecutorType,
        "MySQL Native",
        "Runs the built-in MySQL compatibility and authentication probes.",
        [DatabaseEngine.MySql],
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
            DatabaseEngine.MySql);

        var unsupported = checks.FirstOrDefault(check => !check.SupportedEngines.Any(engine =>
            engine is DatabaseEngine.MySql or DatabaseEngine.Generic));
        if (unsupported is not null)
        {
            throw new InvalidOperationException(
                $"MySQL compatibility executor does not support check '{unsupported.Name}'.");
        }

        // The current runtime performs mysql-info preflight and retains the
        // mysql_native_password NSE fallback. This strategy owns the complete
        // MySQL compatibility group so the preflight is executed only once.
        var scripts = checks.Select(check => check.ToLegacyNseScript()).ToArray();
        var result = await scannerRuntime.RunAsync(
            context.Scan,
            context.Target,
            scripts,
            context.Authentication,
            cancellationToken);
        var genericResult = result.ToCompositeScanExecutionResult(ExecutorType);

        return new ScanExecutorResult(
            genericResult.StructuredOutput,
            genericResult.CheckResults,
            genericResult.Artifacts);
    }
}
