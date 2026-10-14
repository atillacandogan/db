using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Application.Observability;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Application.Services;

public sealed class ScanProcessingService(
    IScanRepository scanRepository,
    INseScriptRepository nseScriptRepository,
    IRuleRepository ruleRepository,
    IScannerRuntime scannerRuntime,
    IRuleEngine ruleEngine,
    IScoringService scoringService) : IScanProcessingService
{
    public Task ProcessAsync(Guid scanId, CancellationToken cancellationToken = default) =>
        ProcessAsync(scanId, selectedCheckIds: null, authentication: null, cancellationToken);

    public async Task ProcessAsync(
        Guid scanId,
        IReadOnlyCollection<string>? selectedCheckIds,
        CancellationToken cancellationToken) =>
        await ProcessAsync(scanId, selectedCheckIds, authentication: null, cancellationToken);

    public async Task ProcessAsync(
        Guid scanId,
        IReadOnlyCollection<string>? selectedCheckIds,
        DatabaseAuthentication? authentication,
        CancellationToken cancellationToken)
    {
        var scan = await scanRepository.GetByIdAsync(scanId, cancellationToken)
            ?? throw new InvalidOperationException($"Scan {scanId} was not found.");

        if (scan.Status == ScanStatus.Cancelled)
        {
            return;
        }

        var target = scan.Target
            ?? throw new InvalidOperationException($"Scan {scanId} target was not loaded.");

        try
        {
            var rules = await ruleRepository.ListEnabledByEngineAsync(target.Engine, cancellationToken);
            var scripts = await SelectChecksAsync(target.Engine, rules, selectedCheckIds, cancellationToken);
            var executionResult = await scannerRuntime.RunAsync(
                scan,
                target,
                scripts,
                authentication,
                cancellationToken);
            var findings = await ruleEngine.EvaluateAsync(scan, target, rules, executionResult, cancellationToken);
            var riskScore = scoringService.CalculateRiskScore(findings);
            var overallSeverity = scoringService.CalculateOverallSeverity(findings, riskScore);

            var scriptResults = executionResult.ScriptResults
                .Select(result => new ScanScriptResult
                {
                    ScanId = scan.Id,
                    NseScriptId = result.NseScriptId,
                    ScanCheckId = result.NseScriptId,
                    ScriptName = result.ScriptName,
                    RawXml = result.RawXml,
                    NormalizedOutput = result.NormalizedOutput,
                    ExitCode = result.ExitCode,
                    ResultStatus = result.ResultStatus
                })
                .ToArray();

            foreach (var _ in scriptResults)
            {
                ScanCompatibilityTelemetry.RecordDualWrite("scan-result");
            }

            await scanRepository.StoreExecutionArtifactsAsync(
                scan.Id,
                scriptResults,
                findings.ToArray(),
                riskScore,
                overallSeverity,
                ScanStatus.Scanned,
                errorMessage: null,
                cancellationToken);

            await scanRepository.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ScanLeaseLostException)
        {
            // The new owner (or cancellation) is authoritative; never mark its job failed.
            scanRepository.ClearTracking();
            throw;
        }
        catch (TimeoutException exception)
        {
            scanRepository.ClearTracking();
            await scanRepository.UpdateStatusAsync(scan.Id, ScanStatus.Timeout, exception.Message, cancellationToken);
            await scanRepository.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            scanRepository.ClearTracking();
            await scanRepository.UpdateStatusAsync(scan.Id, ScanStatus.Failed, exception.Message, cancellationToken);
            await scanRepository.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<IReadOnlyCollection<NseScript>> SelectChecksAsync(
        DatabaseEngine engine,
        IReadOnlyCollection<Rule> rules,
        IReadOnlyCollection<string>? selectedCheckIds,
        CancellationToken cancellationToken)
    {
        if (selectedCheckIds is not null)
        {
            var requestedCheckIds = selectedCheckIds
                .Where(checkId => !string.IsNullOrWhiteSpace(checkId))
                .Select(checkId => checkId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (requestedCheckIds.Length == 0)
            {
                throw new InvalidOperationException("At least one scan check must be selected.");
            }

            var requestedChecks = await nseScriptRepository.ListRunnableByIdsAsync(engine, requestedCheckIds, cancellationToken);
            var selectedKeys = requestedChecks
                .SelectMany(script => new[] { script.Id, script.NseScriptName })
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unavailableCheckIds = requestedCheckIds
                .Where(checkId => !selectedKeys.Contains(checkId))
                .ToArray();

            if (unavailableCheckIds.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Selected scan checks are not available for the target engine: {string.Join(", ", unavailableCheckIds)}.");
            }

            return requestedChecks;
        }

        var approvedChecks = await nseScriptRepository.ListApprovedByEngineAsync(engine, cancellationToken);

        var selectedCheckIdsFromRules = rules
            .SelectMany(rule => rule.RuleScripts)
            .Select(mapping => ScanCheckIdCompatibility.Resolve(
                mapping.ScanCheckId,
                mapping.NseScriptId,
                "scan-selection")!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selectedChecks = approvedChecks
            .Where(check => check.DefaultEnabled || selectedCheckIdsFromRules.Contains(check.Id))
            .GroupBy(check => check.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        return selectedChecks;
    }
}
