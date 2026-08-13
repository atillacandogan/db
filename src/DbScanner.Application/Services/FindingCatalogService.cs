using DbScanner.Application.Ports;
using DbScanner.Contracts.Findings;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Application.Services;

public sealed class FindingCatalogService(IFindingRepository findingRepository) : IFindingCatalogService
{
    public async Task<IReadOnlyList<FindingListItemDto>> ListLatestAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        var findings = await findingRepository.ListLatestAsync(limit, cancellationToken);
        return findings.Select(MapFinding).ToArray();
    }

    public async Task<IReadOnlyList<FindingListItemDto>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        var findings = await findingRepository.ListAllAsync(cancellationToken);
        return findings.Select(MapFinding).ToArray();
    }

    private static FindingListItemDto MapFinding(Finding finding) =>
        new(
            finding.Id,
            finding.ScanId,
            finding.TargetId,
            finding.Target?.Name ?? finding.TargetId.ToString(),
            finding.Target?.Host ?? "-",
            ToEngine(finding.Target?.Engine ?? DatabaseEngine.Generic),
            finding.RuleId,
            finding.Title,
            ToSeverity(finding.Severity),
            finding.ScoreContribution,
            finding.Evidence,
            finding.Remediation,
            finding.SourceScript,
            ToScanStatus(finding.Scan?.Status ?? ScanStatus.Pending),
            finding.Scan?.RiskScore,
            finding.Scan?.OverallSeverity?.ToString().ToLowerInvariant(),
            finding.DetectedAt);

    private static string ToSeverity(Severity severity) => severity.ToString().ToLowerInvariant();

    private static string ToEngine(DatabaseEngine engine) => engine.ToApiString();

    private static string ToScanStatus(ScanStatus status) => status switch
    {
        ScanStatus.Pending => "pending",
        ScanStatus.Scanning => "scanning",
        ScanStatus.Scanned => "scanned",
        ScanStatus.Failed => "failed",
        ScanStatus.Timeout => "timeout",
        ScanStatus.Cancelled => "cancelled",
        _ => "pending"
    };
}
