using DbScanner.Domain.Enums;

namespace DbScanner.Domain.Entities;

public sealed class ScanScopeAllowlist
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string ScopeValue { get; set; }
    public ScanScopeType ScopeType { get; set; }
    public int? Port { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
