namespace DbScanner.Application.Models;

public sealed record CreateRuleCommand(
    string Id,
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
    IReadOnlyList<string>? NseScripts = null,
    IReadOnlyList<string>? ScanChecks = null);
