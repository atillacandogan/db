using DbScanner.Domain.Enums;

namespace DbScanner.Domain.Entities;

public sealed class Rule
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public DatabaseEngine Engine { get; set; } = DatabaseEngine.Generic;
    public Severity Severity { get; set; }
    public int Weight { get; set; }
    public bool Enabled { get; set; } = true;
    public ParserType ParserType { get; set; } = ParserType.GenericOutput;
    public MatcherType MatcherType { get; set; } = MatcherType.Contains;
    public required string MatcherExpression { get; set; }
    public required string Remediation { get; set; }

    public ICollection<RuleEngineSupport> EngineSupport { get; } = new List<RuleEngineSupport>();
    public ICollection<RuleNseScript> RuleScripts { get; } = new List<RuleNseScript>();
    public ICollection<Finding> Findings { get; } = new List<Finding>();
}
