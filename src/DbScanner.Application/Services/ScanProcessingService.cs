using DbScanner.Application.Models;
using DbScanner.Application.Ports;
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
        ProcessAsync(scanId, selectedNseScriptIds: null, authentication: null, cancellationToken);

    public async Task ProcessAsync(
        Guid scanId,
        IReadOnlyCollection<string>? selectedNseScriptIds,
        CancellationToken cancellationToken) =>
        await ProcessAsync(scanId, selectedNseScriptIds, authentication: null, cancellationToken);

    public async Task ProcessAsync(
        Guid scanId,
        IReadOnlyCollection<string>? selectedNseScriptIds,
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
            var scripts = await SelectScriptsAsync(target.Engine, rules, selectedNseScriptIds, cancellationToken);
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
                    ScriptName = result.ScriptName,
                    RawXml = result.RawXml,
                    NormalizedOutput = result.NormalizedOutput,
                    ExitCode = result.ExitCode,
                    ResultStatus = result.ResultStatus
                })
                .ToArray();

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

    private async Task<IReadOnlyCollection<NseScript>> SelectScriptsAsync(
        DatabaseEngine engine,
        IReadOnlyCollection<Rule> rules,
        IReadOnlyCollection<string>? selectedNseScriptIds,
        CancellationToken cancellationToken)
    {
        if (selectedNseScriptIds is not null)
        {
            var requestedScriptIds = selectedNseScriptIds
                .Where(scriptId => !string.IsNullOrWhiteSpace(scriptId))
                .Select(scriptId => scriptId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (requestedScriptIds.Length == 0)
            {
                throw new InvalidOperationException("At least one NSE script must be selected.");
            }

            var requestedScripts = await nseScriptRepository.ListRunnableByIdsAsync(engine, requestedScriptIds, cancellationToken);
            var selectedKeys = requestedScripts
                .SelectMany(script => new[] { script.Id, script.NseScriptName })
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unavailableScriptIds = requestedScriptIds
                .Where(scriptId => !selectedKeys.Contains(scriptId))
                .ToArray();

            if (unavailableScriptIds.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Selected NSE scripts are not available for the target engine: {string.Join(", ", unavailableScriptIds)}.");
            }

            return requestedScripts;
        }

        var approvedScripts = await nseScriptRepository.ListApprovedByEngineAsync(engine, cancellationToken);

        var selectedScriptIds = rules
            .SelectMany(rule => rule.RuleScripts)
            .Select(mapping => mapping.NseScriptId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selectedScripts = approvedScripts
            .Where(script => script.DefaultEnabled || selectedScriptIds.Contains(script.Id))
            .GroupBy(script => script.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        return selectedScripts;
    }
}
