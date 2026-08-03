using DbScanner.Domain.Enums;

namespace DbScanner.Domain.Entities;

public sealed class RuleEngineSupport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string RuleId { get; set; }
    public Rule? Rule { get; set; }
    public DatabaseEngine Engine { get; set; }
    public RuleResultStatus UnsupportedStatus { get; set; } = RuleResultStatus.NotSupported;
}
