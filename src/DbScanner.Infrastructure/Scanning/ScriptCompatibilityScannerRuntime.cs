using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Infrastructure.Scanning;

internal sealed class ScriptCompatibilityScannerRuntime(
    NmapScannerRuntime scannerRuntime) : IScannerRuntime
{
    public async Task<ScanExecutionResult> RunAsync(
        Scan scan,
        Target target,
        IReadOnlyCollection<NseScript> scripts,
        DatabaseAuthentication? authentication = null,
        CancellationToken cancellationToken = default)
    {
        var compatibleScripts = scripts
            .Where(script => IsCompatible(script, target.Engine))
            .ToArray();
        var incompatibleScripts = scripts
            .Where(script => !IsCompatible(script, target.Engine))
            .ToArray();

        var result = await scannerRuntime.RunAsync(
            scan,
            target,
            compatibleScripts,
            authentication,
            cancellationToken);

        if (incompatibleScripts.Length == 0)
        {
            return result;
        }

        var skippedResults = incompatibleScripts
            .Select(script => new ScanScriptExecutionResult(
                script.Id,
                script.NseScriptName,
                null,
                $"status=skipped; reason=Script is not compatible with {ToDisplayName(target.Engine)} target.",
                0,
                ProbeResultStatus.Skipped))
            .ToArray();

        return result with
        {
            ScriptResults = result.ScriptResults.Concat(skippedResults).ToArray()
        };
    }

    internal static bool IsCompatible(NseScript script, DatabaseEngine engine)
    {
        var engineToken = engine.ToApiString();
        return script.SupportedEngines
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(token =>
                token.Equals("generic", StringComparison.OrdinalIgnoreCase)
                || token.Equals(engineToken, StringComparison.OrdinalIgnoreCase));
    }

    private static string ToDisplayName(DatabaseEngine engine) => engine.ToDisplayName();
}
