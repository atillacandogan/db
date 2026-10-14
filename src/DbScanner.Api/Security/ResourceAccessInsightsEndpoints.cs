using DbScanner.Application.Exceptions;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Infrastructure.Identity;
using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DbScanner.Api.Security;

public sealed record AccessTeamArchiveRequest(bool Archived);

public static class ResourceAccessInsightsEndpoints
{
    public static void MapResourceAccessInsights(this RouteGroupBuilder group)
    {
        group.MapGet("/preview", async (string subject, Guid targetId, DbScannerDbContext db,
            IKeycloakAdminService identity, IOptions<TenantIsolationOptions> tenancy, CancellationToken ct) =>
        {
            if (tenancy.Value.DedicatedTenant) return Results.Json(new { message = "Tenant kapsamlı bir kimlik dizini olmadan kullanıcı izinleri önizlenemez." }, statusCode: 403);
            if (string.IsNullOrWhiteSpace(subject) || subject.Length > 256 || subject != subject.Trim())
                return Results.BadRequest(new { message = "Mevcut bir Keycloak kullanıcı ID'si seçin." });
            var target = await db.Targets.AsNoTracking().SingleOrDefaultAsync(t => t.Id == targetId, ct);
            if (target is null) return Results.NotFound(new { message = "Hedef bulunamadı." });
            IdentityUser? user;
            try { user = await identity.GetUserAsync(subject, ct); }
            catch (Exception error) when (error is IdentityManagementUnavailableException or InvalidOperationException)
            { return Results.Json(new { message = "Keycloak kullanıcı bilgileri alınamadı. İzin önizlemesi hesaplanamadı." }, statusCode: 503); }
            if (user is null || user.Id != subject) return Results.NotFound(new { message = "Kullanıcı Keycloak'ta bulunamadı." });
            var sources = await (from grant in db.AccessGrants.AsNoTracking()
                join team in db.AccessTeams.AsNoTracking() on grant.TeamId equals team.Id
                where grant.ProjectId == target.ProjectId && (grant.TargetId == null || grant.TargetId == targetId)
                    && db.AccessTeamMembers.Any(m => m.TeamId == team.Id && m.Subject == subject)
                orderby team.Name
                select new { grant.Id, grant.TeamId, teamName = team.Name, team.IsArchived,
                    grant.ProjectId, grant.TargetId, grant.Permissions }).ToListAsync(ct);
            var activePermissions = sources.Where(s => !s.IsArchived).Select(s => s.Permissions).ToArray();
            var effective = TargetAccessPolicy.EffectivePermissions(user.Role, user.Enabled, activePermissions);
            var ceiling = TargetAccessPolicy.RolePermissions(user.Role);
            var teamPermissions = activePermissions.Where(p => p.HasFlag(TargetPermission.Read))
                .Aggregate(TargetPermission.None, (permissions, grant) => permissions | grant);
            var decisions = new[] { TargetPermission.Read, TargetPermission.Scan, TargetPermission.Manage, TargetPermission.Export }
                .Select(permission => new { permission, allowed = effective.HasFlag(permission), reason =
                    !user.Enabled ? "Hesap devre dışı."
                    : ceiling == TargetPermission.None ? "DbScanner uygulama rolü atanmadı."
                    : user.Role == "dbscanner-admin" ? "Yöneticiler kurumdaki tüm hedeflere erişebilir."
                    : !ceiling.HasFlag(permission) ? "Kullanıcının rolü bu işleme izin vermiyor."
                    : !teamPermissions.HasFlag(permission) ? "Bu hedef için aktif ekip izni bulunmuyor."
                    : "Aktif ekip izni ve kullanıcının rolü bu işleme izin veriyor." });
            return Results.Ok(new { user = new { user.Id, user.Username, user.Role, user.Enabled },
                target = new { target.Id, target.Name, target.ProjectId }, rolePermissions = ceiling,
                teamPermissions, effectivePermissions = effective, sources, decisions, evaluatedAt = DateTimeOffset.UtcNow,
                basis = "Güncel Keycloak rolü ve hesap durumuna göre yeni veya yenilenmiş bir oturum için hesaplanır. Rol değişikliğinden sonra mevcut token yenilenene kadar farklı olabilir." });
        });

        group.MapGet("/teams/{id:guid}", async (Guid id, DbScannerDbContext db, CancellationToken ct) =>
        {
            var team = await db.AccessTeams.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
            if (team is null) return Results.NotFound();
            return Results.Ok(new { team,
                members = await db.AccessTeamMembers.AsNoTracking().Where(m => m.TeamId == id).OrderBy(m => m.Subject).ToListAsync(ct),
                grants = await db.AccessGrants.AsNoTracking().Where(g => g.TeamId == id).ToListAsync(ct),
                projects = await db.AccessProjects.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct),
                targets = await db.Targets.AsNoTracking().Select(t => new { t.Id, t.Name, t.ProjectId }).ToListAsync(ct) });
        });
        group.MapPut("/teams/{id:guid}", async (Guid id, AccessNameRequest request, DbScannerDbContext db,
            HttpContext http, ResourceAccessContext access, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160)
                return Results.BadRequest(new { message = "Ekip adı 1 ile 160 karakter arasında olmalıdır." });
            var team = await db.AccessTeams.SingleOrDefaultAsync(t => t.Id == id, ct);
            if (team is null) return Results.NotFound();
            if (team.Name == request.Name.Trim()) return Results.Ok(team);
            var before = new { team.Id, team.Name, team.IsArchived };
            team.Name = request.Name.Trim();
            ResourceAccessAuditWriter.Record(db, http, access, "team.renamed", "team", id.ToString(), id,
                before, new { team.Id, team.Name, team.IsArchived });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return ConcurrentChange(); }
            return Results.Ok(team);
        });
        group.MapPut("/teams/{id:guid}/archive", async (Guid id, AccessTeamArchiveRequest request, DbScannerDbContext db,
            HttpContext http, ResourceAccessContext access, CancellationToken ct) =>
        {
            var team = await db.AccessTeams.SingleOrDefaultAsync(t => t.Id == id, ct);
            if (team is null) return Results.NotFound();
            if (team.IsArchived == request.Archived) return Results.Ok(team);
            var before = new { team.Id, team.Name, team.IsArchived };
            team.IsArchived = request.Archived;
            ResourceAccessAuditWriter.Record(db, http, access, request.Archived ? "team.archived" : "team.restored", "team", id.ToString(), id,
                before, new { team.Id, team.Name, team.IsArchived });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return ConcurrentChange(); }
            return Results.Ok(team);
        });
        group.MapGet("/audit", async (Guid? teamId, string? action, string? entityType, string? entityId, int? page, DbScannerDbContext db, CancellationToken ct) =>
        {
            if (page is < 1 or > 10000 || (action?.Length ?? 0) > 64 || (entityType?.Length ?? 0) > 32 || (entityId?.Length ?? 0) > 256)
                return Results.BadRequest(new { message = "Geçersiz denetim geçmişi filtresi veya sayfa." });
            var query = db.ResourceAccessAudits.AsNoTracking();
            if (teamId.HasValue) query = query.Where(a => a.TeamId == teamId);
            if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action == action);
            if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(a => a.EntityType == entityType);
            if (!string.IsNullOrWhiteSpace(entityId)) query = query.Where(a => a.EntityId == entityId);
            const int pageSize = 20;
            var totalCount = await query.CountAsync(ct);
            var items = await query.OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id)
                .Skip(((page ?? 1) - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return Results.Ok(new { items, totalCount, page = page ?? 1, pageSize });
        });
    }

    private static IResult ConcurrentChange() => Results.Conflict(new { message = "Ekip başka bir işlem tarafından değiştirildi. Sayfayı yenileyip tekrar deneyin." });
}
