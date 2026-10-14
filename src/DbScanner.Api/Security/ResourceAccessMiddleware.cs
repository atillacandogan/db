using DbScanner.Application.Models;
using DbScanner.Domain.Entities;
using DbScanner.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace DbScanner.Api.Security;

public sealed class ResourceAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, ResourceAccessContext access, IOptions<TenantIsolationOptions> tenancy)
    {
        if (!http.Request.Path.StartsWithSegments("/api") || http.Request.Path == "/api/status")
        {
            await next(http);
            return;
        }
        if (http.User.Identity?.IsAuthenticated != true)
        {
            http.Response.StatusCode = 401;
            return;
        }
        var subject = http.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject) || !tenancy.Value.Allows(http.User))
        {
            await Deny(http, "TENANT_ACCESS_DENIED", "Bu kuruma erişiminiz yok.");
            return;
        }
        access.Subject = subject;
        access.IsAdministrator = http.User.IsInRole(AuthorizationRoles.Admin);
        access.RolePermissions = TargetAccessPolicy.RolePermissions(access.IsAdministrator ? AuthorizationRoles.Admin
            : http.User.IsInRole(AuthorizationRoles.Operator) ? AuthorizationRoles.Operator
            : http.User.IsInRole(AuthorizationRoles.Viewer) ? AuthorizationRoles.Viewer : null);
        var path = NormalizePath(http.Request.Path.Value ?? "");
        // Discovery and scope lists expose the organization's network inventory.
        // Until project-scoped discovery exists, keep this surface admin-only.
        if ((!access.IsAdministrator && (path.StartsWith("/api/discover", StringComparison.Ordinal)
                || (http.Request.Method == "POST" && path == "/api/targets")))
            || (tenancy.Value.DedicatedTenant && path.StartsWith("/api/admin/users", StringComparison.Ordinal)))
        {
            await Deny(http, "RESOURCE_ACCESS_DENIED", "Bu işlem kurum yöneticisi yetkisi gerektirir veya kimlik yönetimi harici olarak yapılmalıdır.");
            return;
        }
        // A customer admin may not expand the scanner's network boundary.
        // Dedicated deployment allowlists are provisioned by platform operations.
        if (tenancy.Value.DedicatedTenant &&
            ((path.StartsWith("/api/discovery-allowlist", StringComparison.Ordinal) && http.Request.Method != "GET")
                || path.StartsWith("/api/discoveries/local-network", StringComparison.Ordinal)
                || path.EndsWith("/allowlist", StringComparison.Ordinal)))
        {
            await Deny(http, "PLATFORM_SCOPE_REQUIRED", "Kurumun ağ sınırları platform yöneticileri tarafından yapılandırılır.");
            return;
        }
        access.RequiredPermission = PermissionFor(http.Request.Method, path);
        try { await next(http); }
        catch (UnauthorizedAccessException) when (!http.Response.HasStarted)
        {
            await Deny(http, "RESOURCE_ACCESS_DENIED", "Bu hedef üzerinde bu işlemi yapma yetkiniz yok.");
        }
    }

    public static TargetPermission PermissionFor(string method, string path)
    {
        path = NormalizePath(path);
        return path.EndsWith("/export", StringComparison.Ordinal) ? TargetPermission.Read | TargetPermission.Export
        : method == "GET" ? TargetPermission.Read
        : path.StartsWith("/api/scans", StringComparison.Ordinal) && method == "POST" ? TargetPermission.Read | TargetPermission.Scan
        : TargetPermission.Read | TargetPermission.Manage;
    }

    private static string NormalizePath(string path) => path.TrimEnd('/').ToLowerInvariant();

    private static Task Deny(HttpContext http, string code, string message)
    {
        http.Response.StatusCode = 403;
        return http.Response.WriteAsJsonAsync(new { code, message });
    }
}
