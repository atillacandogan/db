namespace DbScanner.Contracts.Rules;

public sealed record RuleDto(
    string Id,
    string Title,
    string Description,
    string Severity,
    int Weight,
    bool Enabled,
    string Engine,
    string ParserType,
    string MatcherType,
    string MatcherExpression,
    string Remediation,
    IReadOnlyList<string> SupportedEngines,
    IReadOnlyList<string> NseScripts);
