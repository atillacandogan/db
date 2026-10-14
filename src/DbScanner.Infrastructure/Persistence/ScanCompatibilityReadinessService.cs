using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Application.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DbScanner.Infrastructure.Persistence;

public sealed class ScanCompatibilityReadinessService(
    DbScannerDbContext dbContext,
    ILogger<ScanCompatibilityReadinessService> logger) : IScanCompatibilityReadinessService
{
    public async Task<ScanCompatibilityReadiness> GetAsync(CancellationToken cancellationToken = default)
    {
        var readiness = new ScanCompatibilityReadiness(
            await dbContext.NseScripts.CountAsync(cancellationToken),
            await dbContext.NseScripts.CountAsync(
                script => script.ExecutorType == null || script.ExecutorType == "",
                cancellationToken),
            await dbContext.RuleNseScripts.CountAsync(cancellationToken),
            await dbContext.RuleNseScripts.CountAsync(
                mapping => mapping.ScanCheckId == null || mapping.ScanCheckId == "",
                cancellationToken),
            await dbContext.RuleNseScripts.CountAsync(
                mapping => mapping.ScanCheckId != null
                    && mapping.ScanCheckId != ""
                    && mapping.ScanCheckId != mapping.NseScriptId,
                cancellationToken),
            await dbContext.ScanScriptResults.CountAsync(cancellationToken),
            await dbContext.ScanScriptResults.CountAsync(
                result => result.ScanCheckId == null || result.ScanCheckId == "",
                cancellationToken),
            await dbContext.ScanScriptResults.CountAsync(
                result => result.ScanCheckId != null
                    && result.ScanCheckId != ""
                    && result.NseScriptId != null
                    && result.ScanCheckId != result.NseScriptId,
                cancellationToken));

        ScanCompatibilityTelemetry.RecordReadiness(readiness.ReadyForLegacyRemoval);

        if (readiness.ReadyForLegacyRemoval)
        {
            logger.LogDebug(
                "Scan-check compatibility readiness passed. CatalogRows={CatalogRows}, RuleMappings={RuleMappings}, ScanResults={ScanResults}",
                readiness.CatalogRows,
                readiness.RuleMappings,
                readiness.ScanResults);
        }
        else
        {
            logger.LogWarning(
                "Scan-check compatibility readiness failed. MissingExecutors={MissingExecutors}, MissingRuleIds={MissingRuleIds}, MismatchedRuleIds={MismatchedRuleIds}, MissingResultIds={MissingResultIds}, MismatchedResultIds={MismatchedResultIds}",
                readiness.CatalogRowsMissingExecutorType,
                readiness.RuleMappingsMissingScanCheckId,
                readiness.RuleMappingsWithMismatchedIds,
                readiness.ScanResultsMissingScanCheckId,
                readiness.ScanResultsWithMismatchedIds);
        }

        return readiness;
    }
}
