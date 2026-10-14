using DbScanner.Domain.Enums;

namespace DbScanner.Domain.Entities;

public sealed class ScanScriptResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScanId { get; set; }
    public Scan? Scan { get; set; }
    public string? NseScriptId { get; set; }
    public string? ScanCheckId { get; set; }
    public NseScript? NseScript { get; set; }
    public required string ScriptName { get; set; }
    public string? RawXml { get; set; }
    public string? NormalizedOutput { get; set; }
    public int? ExitCode { get; set; }
    public ProbeResultStatus? ResultStatus { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
