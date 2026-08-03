namespace DbScanner.Api.Contracts;

public sealed record UpdateNseScriptRequest(
    string DisplayName,
    string Description,
    IReadOnlyList<string> SupportedEngines,
    bool DefaultEnabled,
    string ApprovalStatus,
    string MatcherType,
    string ParserType,
    string MatcherExpression,
    string Severity,
    int Weight,
    string? Categories,
    string? RemediationTemplate,
    string? DocUrl);
