using DbScanner.Domain.Enums;

namespace DbScanner.Domain.Entities;

public sealed class NseScriptMatcher
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string NseScriptId { get; set; }
    public NseScript? NseScript { get; set; }
    public string? RuleId { get; set; }
    public Rule? Rule { get; set; }
    public ParserType ParserType { get; set; } = ParserType.GenericOutput;
    public MatcherType MatcherType { get; set; } = MatcherType.Contains;
    public required string MatcherExpression { get; set; }
    public DatabaseEngine Engine { get; set; } = DatabaseEngine.Generic;
    public bool Enabled { get; set; } = true;
}
