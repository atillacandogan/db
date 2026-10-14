namespace DbScanner.Domain.Entities;

[Flags]
public enum TargetPermission { None = 0, Read = 1, Scan = 2, Manage = 4, Export = 8, All = 15 }

public sealed class AccessProject
{
    public static readonly Guid DefaultId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
}

public sealed class AccessTeam
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public bool IsArchived { get; set; }
}

// Committed authorization changes only. No identity secrets or target credentials.
public sealed class ResourceAccessAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string ActorSubject { get; set; }
    public string? ActorUsername { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public Guid? TeamId { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AccessTeamMember
{
    public Guid TeamId { get; set; }
    // Immutable OIDC subject, never a user-supplied display name/email.
    public required string Subject { get; set; }
}

public sealed class AccessGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TeamId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid? TargetId { get; set; }
    public TargetPermission Permissions { get; set; }
}
