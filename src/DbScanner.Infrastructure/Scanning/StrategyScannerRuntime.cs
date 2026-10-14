using System.Xml.Linq;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;

namespace DbScanner.Infrastructure.Scanning;

/// <summary>
/// Executes scans exclusively through the registered executor strategies.
/// Converts their artifacts for the existing XML rule-engine boundary.
/// </summary>
internal sealed class StrategyScannerRuntime(
    INseScriptScanCheckRouter checkRouter,
    IScanExecutionOrchestrator orchestrator) : IScannerRuntime
{
    public async Task<ScanExecutionResult> RunAsync(
        Scan scan,
        Target target,
        IReadOnlyCollection<NseScript> scripts,
        DatabaseAuthentication? authentication = null,
        CancellationToken cancellationToken = default)
    {
        var checks = checkRouter.Route(scripts);
        var hasMySqlCompatibilityExecutor = checks.Any(check => string.Equals(
            check.ExecutorType,
            ScanExecutorTypes.MySqlNative,
            StringComparison.Ordinal));
        var requiredExecutorTypes = hasMySqlCompatibilityExecutor
            ? Array.Empty<string>()
            : new[] { ScanExecutorTypes.NmapNse };
        var context = new ScanExecutionContext(
            scan,
            target,
            authentication,
            requiredExecutorTypes);
        var result = await orchestrator.ExecuteAsync(
            context,
            checks,
            cancellationToken);

        return ToLegacyExecutionResult(result);
    }

    private static ScanExecutionResult ToLegacyExecutionResult(
        CompositeScanExecutionResult result)
    {
        var xmlArtifacts = result.Artifacts
            .Where(artifact =>
                artifact.PayloadFormat == ScanPayloadFormat.Xml
                && !string.IsNullOrWhiteSpace(artifact.RawPayload))
            .ToArray();
        if (xmlArtifacts.Length == 0)
        {
            throw new InvalidOperationException(
                "Strategy execution did not produce the XML baseline artifact required by the legacy rule engine.");
        }

        var rawXml = xmlArtifacts.Length == 1
            ? xmlArtifacts[0].RawPayload!
            : MergeXmlArtifacts(xmlArtifacts);
        var scriptResults = result.CheckResults
            .Select(checkResult => checkResult.ToLegacyScanScriptExecutionResult())
            .ToArray();

        return new ScanExecutionResult(
            rawXml,
            result.StructuredOutput,
            scriptResults);
    }

    private static string MergeXmlArtifacts(
        IReadOnlyCollection<ScanExecutionArtifact> artifacts)
    {
        var root = new XElement(
            "dbscanner-execution",
            artifacts.Select(artifact => new XElement(
                "artifact",
                new XAttribute("executor", artifact.ExecutorType),
                new XElement(XDocument.Parse(artifact.RawPayload!).Root
                    ?? throw new InvalidOperationException(
                        $"Executor '{artifact.ExecutorType}' returned empty XML.")))));

        return root.ToString(SaveOptions.DisableFormatting);
    }
}
