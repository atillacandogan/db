namespace DbScanner.Contracts.NseScripts;

public sealed record NseScriptDto(
    string Id,
    string NseScriptName,
    string DisplayName,
    IReadOnlyList<string> SupportedEngines,
    bool DefaultEnabled,
    bool RequiresCredentials,
    bool IsIntrusive,
    string ApprovalStatus,
    string Description,
    string MatcherType,
    string ParserType,
    string MatcherExpression,
    string Severity,
    int Weight,
    string? Categories,
    string? RemediationTemplate,
    string? DocUrl,
    string Source,
    bool? IsInstalled,
    bool CanDelete);
