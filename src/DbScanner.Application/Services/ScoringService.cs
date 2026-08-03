using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Application.Services;

public sealed class ScoringService : IScoringService
{
    public int CalculateRiskScore(IEnumerable<Finding> failedFindings)
    {
        var rawScore = failedFindings.Sum(finding =>
            finding.ScoreContribution > 0
                ? finding.ScoreContribution
                : CalculateFindingContribution(1, finding.Severity));

        return Math.Clamp(rawScore, 0, 100);
    }

    public Severity CalculateOverallSeverity(IEnumerable<Finding> findings, int riskScore)
    {
        var scoreSeverity = riskScore switch
        {
            >= 76 => Severity.Critical,
            >= 51 => Severity.High,
            >= 26 => Severity.Medium,
            >= 1 => Severity.Low,
            _ => Severity.Info
        };

        var highestFindingSeverity = findings
            .Select(finding => finding.Severity)
            .DefaultIfEmpty(Severity.Info)
            .Max();

        return (Severity)Math.Max((int)scoreSeverity, (int)highestFindingSeverity);
    }

    public int CalculateFindingContribution(int ruleWeight, Severity severity)
    {
        var multiplier = severity switch
        {
            Severity.Critical => 5,
            Severity.High => 3,
            Severity.Medium => 2,
            Severity.Low => 1,
            Severity.Info => 0,
            _ => 0
        };

        return ruleWeight * multiplier;
    }
}
