using DbScanner.Domain.Enums;

namespace DbScanner.Domain.Entities;

public sealed class NseScript
{
    public required string Id { get; set; }
    /// <summary>
    /// Executor-neutral strategy identifier. Null means the row predates the
    /// generic catalog and must use the legacy server-side routing rules.
    /// </summary>
    public string? ExecutorType { get; set; }
    public required string NseScriptName { get; set; }
    public required string DisplayName { get; set; }
    public required string Description { get; set; }
    public required string SupportedEngines { get; set; }
    public string? Categories { get; set; }
    public bool DefaultEnabled { get; set; }
    public bool RequiresCredentials { get; set; }
    public bool IsIntrusive { get; set; }
    public string? AllowedArgsSchema { get; set; }
    public ParserType ParserType { get; set; } = ParserType.GenericOutput;
    public MatcherType MatcherType { get; set; } = MatcherType.Contains;
    public string? MatcherExpression { get; set; }
    public Severity Severity { get; set; } = Severity.Info;
    public int Weight { get; set; }
    public string? RemediationTemplate { get; set; }
    public string? DocUrl { get; set; }
    public ScriptApprovalStatus ApprovalStatus { get; set; } = ScriptApprovalStatus.Draft;
    public NseScriptSource Source { get; set; } = NseScriptSource.Manual;
    public bool? IsInstalled { get; set; }

    public ICollection<NseScriptMatcher> Matchers { get; } = new List<NseScriptMatcher>();
    public ICollection<RuleNseScript> RuleScripts { get; } = new List<RuleNseScript>();
    public ICollection<ScanScriptResult> ScanResults { get; } = new List<ScanScriptResult>();
}
