using System.Text.Json;
using DbScanner.Application.Models;
using DbScanner.Domain.Entities;
using DbScanner.Infrastructure.Persistence;

namespace DbScanner.Api.Security;

public static class ResourceAccessAuditWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Call before SaveChanges/commit: audit and mutation must succeed or roll back together.
    // All call sites supply explicit, allowlisted snapshots rather than HTTP bodies/entities.
    public static void Record(DbScannerDbContext db, HttpContext http, ResourceAccessContext access,
        string action, string entityType, string entityId, Guid? teamId, object? before, object? after)
    {
        if (!access.IsAdministrator || string.IsNullOrWhiteSpace(access.Subject))
            throw new UnauthorizedAccessException();
        db.ResourceAccessAudits.Add(new ResourceAccessAudit
        {
            ActorSubject = access.Subject,
            ActorUsername = http.User.FindFirst("preferred_username")?.Value,
            Action = action, EntityType = entityType, EntityId = entityId, TeamId = teamId,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before, JsonOptions),
            AfterJson = after is null ? null : JsonSerializer.Serialize(after, JsonOptions)
        });
    }

    public static object GrantSnapshot(AccessGrant grant) => new
        { grant.Id, grant.TeamId, grant.ProjectId, grant.TargetId, grant.Permissions };
}
