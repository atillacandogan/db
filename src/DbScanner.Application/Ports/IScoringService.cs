using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Application.Ports;

public interface IScoringService
{
    int CalculateRiskScore(IEnumerable<Finding> failedFindings);
    Severity CalculateOverallSeverity(IEnumerable<Finding> findings, int riskScore);
    int CalculateFindingContribution(int ruleWeight, Severity severity);
}
