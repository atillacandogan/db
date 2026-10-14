using DbScanner.Domain.Entities;

namespace DbScanner.Application.Models;

// Request-local; the API fills this only after JWT validation. Workers have an
// explicit system registration and access only their deployment's database.
public sealed class ResourceAccessContext
{
    public bool IsSystem { get; init; }
    public bool IsAdministrator { get; set; }
    public string Subject { get; set; } = string.Empty;
    public TargetPermission RolePermissions { get; set; }
    public TargetPermission RequiredPermission { get; set; } = TargetPermission.Read;
    public static ResourceAccessContext ForSystem() => new() { IsSystem = true };
}
