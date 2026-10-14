using System.Text.RegularExpressions;
using System.Xml.XPath;
using System.Xml.Linq;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Application.Services;

public sealed class RuleEngine(IScoringService scoringService) : IRuleEngine
{
    public Task<IReadOnlyList<Finding>> EvaluateAsync(
        Scan scan,
        Target target,
        IReadOnlyCollection<Rule> rules,
        ScanExecutionResult executionResult,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var findings = new List<Finding>();

        foreach (var rule in rules.Where(rule => rule.Enabled))
        {
            var relatedScriptIds = rule.RuleScripts
                .Select(mapping => ScanCheckIdCompatibility.Resolve(
                    mapping.ScanCheckId,
                    mapping.NseScriptId,
                    "rule-evaluation")!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var relatedResults = executionResult.ScriptResults
                .Where(result => relatedScriptIds.Length == 0
                    || relatedScriptIds.Contains(result.NseScriptId ?? result.ScriptName, StringComparer.OrdinalIgnoreCase)
                    || relatedScriptIds.Contains(result.ScriptName, StringComparer.OrdinalIgnoreCase))
                .ToArray();

            var evidence = rule.ParserType switch
            {
                ParserType.StructuredXml => executionResult.StructuredOutput,
                ParserType.GenericOutput => relatedResults.Length == 0
                    ? string.Empty
                    : string.Join(Environment.NewLine, relatedResults.Select(result => result.NormalizedOutput)),
                ParserType.Custom => string.Empty,
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(evidence) && rule.ParserType != ParserType.StructuredXml)
            {
                continue;
            }

            if (!Matches(rule.MatcherType, rule.MatcherExpression, evidence, executionResult.RawXml))
            {
                continue;
            }

            var sourceScript = relatedResults.FirstOrDefault()?.ScriptName
                ?? relatedScriptIds.FirstOrDefault()
                ?? "nmap";

            findings.Add(new Finding
            {
                ScanId = scan.Id,
                TargetId = target.Id,
                RuleId = rule.Id,
                Title = rule.Title,
                Severity = rule.Severity,
                ScoreContribution = scoringService.CalculateFindingContribution(rule.Weight, rule.Severity),
                Evidence = TrimEvidence(evidence),
                Remediation = rule.Remediation,
                SourceScript = sourceScript
            });
        }

        return Task.FromResult<IReadOnlyList<Finding>>(findings);
    }

    private static bool Matches(
        MatcherType matcherType,
        string expression,
        string evidence,
        string rawXml)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        return matcherType switch
        {
            MatcherType.Contains => evidence.Contains(expression, StringComparison.OrdinalIgnoreCase),
            MatcherType.Regex => Regex.IsMatch(evidence, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            MatcherType.KeyValue => MatchesKeyValue(expression, evidence),
            MatcherType.XPath => MatchesXPath(expression, rawXml),
            _ => false
        };
    }

    private static bool MatchesKeyValue(string expression, string evidence)
    {
        var separatorIndex = expression.IndexOf('=');
        if (separatorIndex <= 0 || separatorIndex == expression.Length - 1)
        {
            return false;
        }

        var key = expression[..separatorIndex].Trim();
        var expectedValue = expression[(separatorIndex + 1)..].Trim();

        foreach (var token in evidence.Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tokenIndex = token.IndexOf('=');
            if (tokenIndex <= 0 || tokenIndex == token.Length - 1)
            {
                continue;
            }

            var tokenKey = token[..tokenIndex].Trim();
            var tokenValue = token[(tokenIndex + 1)..].Trim();
            if (key.Equals(tokenKey, StringComparison.OrdinalIgnoreCase)
                && expectedValue.Equals(tokenValue, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesXPath(string expression, string rawXml)
    {
        if (string.IsNullOrWhiteSpace(rawXml))
        {
            return false;
        }

        try
        {
            var document = XDocument.Parse(rawXml);
            var selected = document.XPathEvaluate(expression);

            return selected switch
            {
                bool boolResult => boolResult,
                IEnumerable<object> nodes => nodes.Any(),
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private static string TrimEvidence(string evidence) =>
        evidence.Length <= 2048 ? evidence : evidence[..2048];
}
