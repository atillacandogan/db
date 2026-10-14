using DbScanner.Domain.Enums;

namespace DbScanner.Application.Models;

public enum CheckApprovalStatus
{
    Draft = 1,
    Approved = 2,
    Disabled = 3
}

/// <summary>
/// Executor-independent description of a scan check.
/// </summary>
public sealed record ScanCheckDefinition(
    string Id,
    string Name,
    string DisplayName,
    string Description,
    string ExecutorType,
    IReadOnlyList<DatabaseEngine> SupportedEngines,
    string? Categories,
    bool DefaultEnabled,
    bool RequiresCredentials,
    bool IsIntrusive,
    string? ConfigurationSchema,
    ParserType ParserType,
    MatcherType MatcherType,
    string? MatcherExpression,
    Severity Severity,
    int Weight,
    string? RemediationTemplate,
    string? DocumentationUrl,
    CheckApprovalStatus ApprovalStatus);
