using DbScanner.Application.Services;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using Xunit;

namespace DbScanner.Application.Tests;

public sealed class ScoringServiceTests
{
    private readonly ScoringService _service = new();

    [Theory]
    [InlineData(Severity.Critical, 5, 25)]
    [InlineData(Severity.High, 4, 12)]
    [InlineData(Severity.Medium, 3, 6)]
    [InlineData(Severity.Low, 2, 2)]
    [InlineData(Severity.Info, 10, 0)]
    public void CalculateFindingContribution_ReturnsExpectedValue(Severity severity, int ruleWeight, int expected)
    {
        var contribution = _service.CalculateFindingContribution(ruleWeight, severity);

        Assert.Equal(expected, contribution);
    }

    [Fact]
    public void CalculateRiskScore_UsesExistingContributionAndClampsToHundred()
    {
        var findings = new[]
        {
            CreateFinding(Severity.Critical, 60),
            CreateFinding(Severity.High, 50)
        };

        var score = _service.CalculateRiskScore(findings);

        Assert.Equal(100, score);
    }

    [Fact]
    public void CalculateRiskScore_ReturnsZeroWhenFindingsHaveNoContribution()
    {
        var findings = new[]
        {
            CreateFinding(Severity.Info, 0)
        };

        var score = _service.CalculateRiskScore(findings);

        Assert.Equal(0, score);
    }

    [Fact]
    public void CalculateOverallSeverity_UsesHighestFindingWhenItExceedsScoreBand()
    {
        var findings = new[]
        {
            CreateFinding(Severity.High, 15),
            CreateFinding(Severity.Low, 1)
        };

        var overallSeverity = _service.CalculateOverallSeverity(findings, riskScore: 16);

        Assert.Equal(Severity.High, overallSeverity);
    }

    [Fact]
    public void CalculateOverallSeverity_UsesScoreBandWhenItExceedsFindings()
    {
        var findings = new[]
        {
            CreateFinding(Severity.Low, 30)
        };

        var overallSeverity = _service.CalculateOverallSeverity(findings, riskScore: 30);

        Assert.Equal(Severity.Medium, overallSeverity);
    }

    [Fact]
    public void CalculateOverallSeverity_ReturnsInfoWhenThereAreNoFindings()
    {
        var overallSeverity = _service.CalculateOverallSeverity([], riskScore: 0);

        Assert.Equal(Severity.Info, overallSeverity);
    }

    private static Finding CreateFinding(Severity severity, int scoreContribution) =>
        new()
        {
            ScanId = Guid.NewGuid(),
            TargetId = Guid.NewGuid(),
            RuleId = "rule",
            Title = "title",
            Severity = severity,
            ScoreContribution = scoreContribution,
            Evidence = "evidence",
            Remediation = "remediation",
            SourceScript = "banner"
        };
}
