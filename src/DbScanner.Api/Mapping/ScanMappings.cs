using DbScanner.Contracts.Scans;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Api.Mapping;

public static class ScanMappings
{
    public static ScanListItemDto ToListItemDto(this Scan scan) =>
        new(
            scan.Id,
            scan.TargetId,
            ToContractStatus(scan.Status),
            scan.RiskScore,
            scan.OverallSeverity is null ? null : ToContractSeverity(scan.OverallSeverity.Value),
            scan.ErrorMessage,
            scan.CreatedAt,
            scan.StartedAt,
            scan.FinishedAt,
            scan.Target is null ? null : new ScanTargetDto(
                scan.Target.Id,
                scan.Target.Name,
                scan.Target.Host,
                scan.Target.Port,
                scan.Target.Engine.ToApiString()));

    public static ScanDto ToDto(this Scan scan) =>
        new(
            scan.Id,
            scan.TargetId,
            ToContractStatus(scan.Status),
            scan.RiskScore,
            scan.OverallSeverity is null ? null : ToContractSeverity(scan.OverallSeverity.Value),
            scan.ErrorMessage,
            scan.CreatedAt,
            scan.StartedAt,
            scan.FinishedAt,
            scan.Target is null ? null : new ScanTargetDto(
                scan.Target.Id,
                scan.Target.Name,
                scan.Target.Host,
                scan.Target.Port,
                scan.Target.Engine.ToApiString()),
            scan.ScriptResults
                .OrderBy(result => result.CreatedAt)
                .Select(result => new ScanScriptResultDto(
                    result.Id,
                    result.NseScriptId,
                    result.ScriptName,
                    result.RawXml,
                    result.NormalizedOutput,
                    result.ExitCode,
                    result.ResultStatus is null ? null : ToContractProbeStatus(result.ResultStatus.Value),
                    result.CreatedAt))
                .ToArray(),
            scan.Findings
                .OrderByDescending(finding => finding.ScoreContribution)
                .ThenBy(finding => finding.Title)
                .Select(finding => new ScanFindingDto(
                    finding.Id,
                    finding.ScanId,
                    finding.TargetId,
                    finding.RuleId,
                    finding.Title,
                    ToContractSeverity(finding.Severity),
                    finding.ScoreContribution,
                    finding.Evidence,
                    finding.Remediation,
                    finding.SourceScript,
                    finding.DetectedAt))
                .ToArray());

    private static string ToContractStatus(ScanStatus status) =>
        status switch
        {
            ScanStatus.Pending => "pending",
            ScanStatus.Scanning => "scanning",
            ScanStatus.Scanned => "scanned",
            ScanStatus.Failed => "failed",
            ScanStatus.Timeout => "timeout",
            ScanStatus.Cancelled => "cancelled",
            _ => "pending"
        };

    private static string ToContractSeverity(Severity severity) =>
        severity switch
        {
            Severity.Critical => "critical",
            Severity.High => "high",
            Severity.Medium => "medium",
            Severity.Low => "low",
            Severity.Info => "info",
            _ => "info"
        };

    private static string ToContractProbeStatus(ProbeResultStatus status) => status switch
    {
        ProbeResultStatus.Passed => "passed",
        ProbeResultStatus.Failed => "failed",
        ProbeResultStatus.Skipped => "skipped",
        ProbeResultStatus.NotSupported => "not_supported",
        ProbeResultStatus.InsufficientPrivileges => "insufficient_privileges",
        ProbeResultStatus.ConnectionFailed => "connection_failed",
        ProbeResultStatus.AuthenticationFailed => "authentication_failed",
        _ => "skipped"
    };
}
