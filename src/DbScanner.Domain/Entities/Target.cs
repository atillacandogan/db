using DbScanner.Domain.Enums;

namespace DbScanner.Domain.Entities;

public sealed class Target
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Host { get; set; }
    public string NormalizedHost { get; set; } = string.Empty;
    public int Port { get; set; }
    public DatabaseEngine Engine { get; set; }
    public TargetConnectionMode ConnectionMode { get; set; } = TargetConnectionMode.DefaultValues;
    public string? EnvironmentTag { get; set; }
    public string? ProtectedCredentials { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Scan> Scans { get; } = new List<Scan>();
    public ICollection<Finding> Findings { get; } = new List<Finding>();
}
