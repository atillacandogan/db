namespace DbScanner.Api.Contracts;

public sealed record UpdateRuleRequest(
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
