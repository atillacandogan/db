namespace DbScanner.Domain.Entities;

public sealed class RuleNseScript
{
    public required string RuleId { get; set; }
    public Rule? Rule { get; set; }
    public required string NseScriptId { get; set; }
    public NseScript? NseScript { get; set; }
}
