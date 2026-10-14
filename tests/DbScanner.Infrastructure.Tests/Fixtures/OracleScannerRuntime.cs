using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Infrastructure.Scanning;

// Frozen pre-Strategy chain retained exclusively for old/new parity tests.
internal sealed class OracleScannerRuntime(
    MsSqlScannerRuntime scannerRuntime,
    IOracleAuthenticatedProbe authenticatedProbe) : IScannerRuntime
{
    public async Task<ScanExecutionResult> RunAsync(
        Scan scan,
        Target target,
        IReadOnlyCollection<NseScript> scripts,
        DatabaseAuthentication? authentication = null,
        CancellationToken cancellationToken = default)
    {
        if (target.Engine != DatabaseEngine.Oracle)
        {
            return await scannerRuntime.RunAsync(
                scan,
                target,
                scripts,
                authentication,
                cancellationToken);
        }

        var authenticatedScripts = scripts
            .Where(script => OracleProbeScripts.IsAuthenticatedProbe(script.NseScriptName))
            .ToArray();
        var discoveryScripts = scripts.Except(authenticatedScripts).ToArray();

        var discoveryResult = await scannerRuntime.RunAsync(
            scan,
            target,
            discoveryScripts,
            authentication,
            cancellationToken);

        if (authenticatedScripts.Length == 0)
        {
            return discoveryResult;
        }

        IReadOnlyList<ScanScriptExecutionResult> authenticatedResults = authentication is null
            ? authenticatedScripts.Select(CreateCredentialsMissingResult).ToArray()
            : await authenticatedProbe.RunAsync(
                target,
                authenticatedScripts,
                authentication,
                cancellationToken);

        return discoveryResult with
        {
            StructuredOutput = string.Join(
                "; ",
                new[] { discoveryResult.StructuredOutput }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Concat(authenticatedResults.Select(
                        result => $"{result.ScriptName}={result.NormalizedOutput}"))),
            ScriptResults = discoveryResult.ScriptResults.Concat(authenticatedResults).ToArray()
        };
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
