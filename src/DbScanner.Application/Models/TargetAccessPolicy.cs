using DbScanner.Domain.Entities;

namespace DbScanner.Application.Models;

public static class TargetAccessPolicy
{
    public static TargetPermission RolePermissions(string? role) => role switch
    {
        "dbscanner-admin" or "dbscanner-operator" => TargetPermission.All,
        "dbscanner-viewer" => TargetPermission.Read | TargetPermission.Export,
        _ => TargetPermission.None
    };

    public static TargetPermission EffectivePermissions(string? role, bool enabled, IEnumerable<TargetPermission> grants)
    {
        if (!enabled) return TargetPermission.None;
        if (role == "dbscanner-admin") return TargetPermission.All;
        var teamPermissions = grants.Where(g => g.HasFlag(TargetPermission.Read))
            .Aggregate(TargetPermission.None, (permissions, grant) => permissions | grant);
        return teamPermissions & RolePermissions(role);
    }
}
