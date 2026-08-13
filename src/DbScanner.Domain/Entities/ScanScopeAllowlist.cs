using DbScanner.Domain.Enums;

namespace DbScanner.Domain.Entities;

public sealed class ScanScopeAllowlist
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string EnvironmentTag { get; set; } = DiscoveryEnvironments.Demo;
    public required string ScopeValue { get; set; }
    public ScanScopeType ScopeType { get; set; }
    public string AllowedEngines { get; set; } = "mysql,postgresql,mssql,oracle";
    public string AllowedPorts { get; set; } = string.Empty;
    public int? Port { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
