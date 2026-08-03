using DbScanner.Application.Models;
using DbScanner.Application.Services;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using Xunit;

namespace DbScanner.Application.Tests;

public sealed class RuleEngineTests
{
    private readonly RuleEngine _engine = new(new ScoringService());

    [Fact]
    public async Task EvaluateAsync_MatchesContainsRuleAndCreatesFinding()
    {
        var scan = new Scan { TargetId = Guid.NewGuid() };
        var target = new Target
        {
            Name = "Demo",
            Host = "demo-postgres-target",
            Port = 5432,
            Engine = DatabaseEngine.PostgreSql
        };

        var rule = CreateRule("contains-rule", MatcherType.Contains, "ssl disabled");
        rule.RuleScripts.Add(new RuleNseScript { RuleId = rule.Id, NseScriptId = "banner" });

        var result = new ScanExecutionResult(
            "<nmaprun />",
            "state=open; service=postgresql",
            new[]
            {
                new ScanScriptExecutionResult("banner", "banner", null, "PostgreSQL server ready; ssl disabled", 0)
            });

        var findings = await _engine.EvaluateAsync(scan, target, new[] { rule }, result);

        Assert.Single(findings);
        Assert.Equal(rule.Id, findings[0].RuleId);
        Assert.Equal(3, findings[0].ScoreContribution);
    }

    [Fact]
    public async Task EvaluateAsync_MatchesKeyValueRuleAgainstStructuredXmlEvidence()
    {
        var scan = new Scan { TargetId = Guid.NewGuid() };
        var target = new Target
        {
            Name = "Demo",
            Host = "demo-postgres-target",
            Port = 5432,
            Engine = DatabaseEngine.PostgreSql
        };

        var rule = CreateRule("kv-rule", MatcherType.KeyValue, "service=postgresql");
        rule.ParserType = ParserType.StructuredXml;

        var result = new ScanExecutionResult(
            "<nmaprun />",
            "state=open; service=postgresql; product=PostgreSQL DB",
            Array.Empty<ScanScriptExecutionResult>());

        var findings = await _engine.EvaluateAsync(scan, target, new[] { rule }, result);

        Assert.Single(findings);
        Assert.Equal("nmap", findings[0].SourceScript);
    }

    [Fact]
    public async Task EvaluateAsync_MatchesXPathRuleAgainstRawXml()
    {
        var scan = new Scan { TargetId = Guid.NewGuid() };
        var target = new Target
        {
            Name = "Demo",
            Host = "demo-mysql-target",
            Port = 3306,
            Engine = DatabaseEngine.MySql
        };

        var rule = CreateRule("xpath-rule", MatcherType.XPath, "//port[@portid='3306']");
        rule.ParserType = ParserType.StructuredXml;

        var result = new ScanExecutionResult(
            """
            <nmaprun>
              <host>
                <ports>
                  <port protocol="tcp" portid="3306" />
                </ports>
              </host>
            </nmaprun>
            """,
            "state=open; service=mysql",
            Array.Empty<ScanScriptExecutionResult>());

        var findings = await _engine.EvaluateAsync(scan, target, new[] { rule }, result);

        Assert.Single(findings);
        Assert.Equal(rule.Id, findings[0].RuleId);
    }

    private static Rule CreateRule(string id, MatcherType matcherType, string expression) =>
        new()
        {
            Id = id,
            Title = id,
            Description = "description",
            Engine = DatabaseEngine.Generic,
            Severity = Severity.High,
            Weight = 1,
            Enabled = true,
            ParserType = ParserType.GenericOutput,
            MatcherType = matcherType,
            MatcherExpression = expression,
            Remediation = "remediation"
        };
}
