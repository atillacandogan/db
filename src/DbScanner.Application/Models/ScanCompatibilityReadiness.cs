namespace DbScanner.Application.Models;

public sealed record ScanCompatibilityReadiness(
    int CatalogRows,
    int CatalogRowsMissingExecutorType,
    int RuleMappings,
    int RuleMappingsMissingScanCheckId,
    int RuleMappingsWithMismatchedIds,
    int ScanResults,
    int ScanResultsMissingScanCheckId,
    int ScanResultsWithMismatchedIds)
{
    public bool ReadyForLegacyRemoval =>
        CatalogRowsMissingExecutorType == 0
        && RuleMappingsMissingScanCheckId == 0
        && RuleMappingsWithMismatchedIds == 0
        && ScanResultsMissingScanCheckId == 0
        && ScanResultsWithMismatchedIds == 0;
}
