namespace DbScanner.Application.Models;

public sealed record UpdateRuleCommand(
    string Title,
    string Description,
    string Engine,
    string Severity,
    int Weight,
    bool Enabled,
    string ParserType,
    string MatcherType,
    string MatcherExpression,
    string Remediation,
    IReadOnlyList<string> SupportedEngines,
    IReadOnlyList<string> NseScripts);
