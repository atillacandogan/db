namespace DbScanner.Domain.Entities;

public sealed class UserManagementAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string ActorSubject { get; set; }
    public string? ActorUsername { get; set; }
    public string? ActorEmail { get; set; }
    public required string Action { get; set; }
    public string? TargetUserId { get; set; }
    public string? PreviousRole { get; set; }
    public string? NewRole { get; set; }
    public bool? PreviousEnabled { get; set; }
    public bool? NewEnabled { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public bool Succeeded { get; set; }
    public string? FailureCode { get; set; }
}
