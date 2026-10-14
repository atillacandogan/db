using DbScanner.Application.Models;
using DbScanner.Application.Exceptions;
using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Identity;
using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DbScanner.Api.Security;

public sealed record AccessNameRequest(string Name);
public sealed record TeamMembershipRequest(string Subject);
public sealed record TeamMembershipBatchRequest(IReadOnlyList<string?>? Subjects);
public sealed record AccessGrantRequest(Guid TeamId, Guid ProjectId, Guid? TargetId, TargetPermission Permissions);
public sealed record TargetProjectRequest(Guid ProjectId);

public static class ResourceAccessEndpoints
{
    public static void MapResourceAccess(this WebApplication app)
    {
        app.MapGet("/api/access/me", (ResourceAccessContext access, IOptions<TenantIsolationOptions> tenancy) =>
            Results.Ok(new { tenantId = tenancy.Value.TenantId, subject = access.Subject,
                administrator = access.IsAdministrator, dedicatedTenant = tenancy.Value.DedicatedTenant,
                permissions = new[] { "Read", "Scan", "Manage", "Export" } }))
            .RequireAuthorization(AuthorizationPolicies.Viewer);

        app.MapGet("/api/access/targets", async (ResourceAccessContext access, DbScannerDbContext db, CancellationToken ct) =>
        {
            var targets = await db.Targets.AsNoTracking().Select(t => new { t.Id, t.ProjectId }).ToListAsync(ct);
            var grants = await db.AccessGrants.AsNoTracking()
                .Where(g => db.AccessTeamMembers.Any(m => m.TeamId == g.TeamId && m.Subject == access.Subject))
                .Where(g => db.AccessTeams.Any(t => t.Id == g.TeamId && !t.IsArchived))
                .ToListAsync(ct);
            return Results.Ok(targets.Select(t => new { targetId = t.Id, permissions = access.IsAdministrator ? TargetPermission.All
                : grants.Where(g => g.ProjectId == t.ProjectId && (g.TargetId == null || g.TargetId == t.Id))
                    .Aggregate(TargetPermission.None, (permissions, g) => permissions | g.Permissions) & access.RolePermissions }));
        }).RequireAuthorization(AuthorizationPolicies.Viewer);

        var group = app.MapGroup("/api/access").RequireAuthorization(AuthorizationPolicies.Admin);
        group.MapResourceAccessInsights();
        group.MapResourceAccessProjects();
        group.MapGet("/users", async (string? search, int? page, IKeycloakAdminService identity,
            IOptions<TenantIsolationOptions> tenancy, CancellationToken ct) =>
        {
            // The current identity adapter is realm-wide, not tenant-scoped.
            if (tenancy.Value.DedicatedTenant) return IdentityScopeUnavailable();
            if ((search?.Length ?? 0) > 160 || page is < 1 or > 10000)
                return Results.BadRequest(new { message = "Arama 160 karakteri aşamaz; sayfa 1 ile 10000 arasında olmalıdır." });
            try
            {
                IdentityUserPage users;
                if (Guid.TryParse(search?.Trim(), out _))
                {
                    var user = await identity.GetUserAsync(search!.Trim(), ct);
                    users = new IdentityUserPage(user is null || page is > 1 ? [] : [user], user is null ? 0 : 1);
                }
                else users = await identity.ListUsersAsync(search?.Trim(), null, null, page ?? 1, 10, ct);
                return Results.Ok(new { items = users.Items.Select(u => new
                    { u.Id, u.Username, u.FirstName, u.LastName, u.Enabled, u.Role }),
                    users.TotalCount, page = page ?? 1, pageSize = 10 });
            }
            catch (Exception error) when (error is IdentityManagementUnavailableException or InvalidOperationException)
            { return IdentityUnavailable(); }
        });
        group.MapGet("/directory", async (DbScannerDbContext db, CancellationToken ct) => Results.Ok(new
        {
            projects = await db.AccessProjects.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct),
            teams = await db.AccessTeams.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct),
            members = await db.AccessTeamMembers.AsNoTracking().OrderBy(m => m.Subject).ToListAsync(ct),
            grants = await db.AccessGrants.AsNoTracking().ToListAsync(ct),
            targets = await db.Targets.AsNoTracking().Select(t => new { t.Id, t.Name, t.ProjectId }).ToListAsync(ct)
        }));
        group.MapPost("/projects", async (AccessNameRequest request, DbScannerDbContext db, HttpContext http, ResourceAccessContext access, CancellationToken ct) =>
        {
            if (!ValidName(request.Name)) return Results.BadRequest(new { message = "Proje adı 1 ile 160 karakter arasında olmalıdır." });
            var project = new AccessProject { Name = request.Name.Trim() };
            db.AccessProjects.Add(project);
            ResourceAccessAuditWriter.Record(db, http, access, "project.created", "project", project.Id.ToString(), null, null, new { project.Id, project.Name });
            await db.SaveChangesAsync(ct);
            return Results.Ok(project);
        });
        group.MapPost("/teams", async (AccessNameRequest request, DbScannerDbContext db, HttpContext http, ResourceAccessContext access, CancellationToken ct) =>
        {
            if (!ValidName(request.Name)) return Results.BadRequest(new { message = "Ekip adı 1 ile 160 karakter arasında olmalıdır." });
            var team = new AccessTeam { Name = request.Name.Trim() };
            db.AccessTeams.Add(team);
            ResourceAccessAuditWriter.Record(db, http, access, "team.created", "team", team.Id.ToString(), team.Id, null, new { team.Id, team.Name, team.IsArchived });
            await db.SaveChangesAsync(ct);
            return Results.Ok(team);
        });
        group.MapPut("/teams/{teamId:guid}/members", async (Guid teamId, TeamMembershipRequest request,
            DbScannerDbContext db, IKeycloakAdminService identity, IOptions<TenantIsolationOptions> tenancy,
            HttpContext http, ResourceAccessContext access, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Length > 256 || request.Subject != request.Subject.Trim())
                return Results.BadRequest(new { message = "Keycloak kullanıcı ID'si (sub) gereklidir." });
            if (tenancy.Value.DedicatedTenant) return IdentityScopeUnavailable();
            string? username = null;
            try
            {
                // Never trust a stale client selection or a manually supplied subject.
                var user = await identity.GetUserAsync(request.Subject, ct);
                if (user is null || user.Id != request.Subject)
                    return Results.NotFound(new { code = "IDENTITY_USER_NOT_FOUND", message = "Kullanıcı Keycloak'ta bulunamadı. Kullanıcıyı listeden yeniden seçin." });
                if (!user.Enabled)
                    return Results.Conflict(new { code = "IDENTITY_USER_DISABLED", message = "Devre dışı bir kullanıcı ekibe eklenemez." });
                username = user.Username;
            }
            catch (Exception error) when (error is IdentityManagementUnavailableException or InvalidOperationException)
            { return IdentityUnavailable(); }
            var team = await db.AccessTeams.SingleOrDefaultAsync(t => t.Id == teamId, ct);
            if (team is null) return Results.NotFound();
            if (team.IsArchived) return Results.Conflict(new { message = "Arşivlenmiş bir ekibe üye eklenemez." });
            if (!await db.AccessTeamMembers.AnyAsync(m => m.TeamId == teamId && m.Subject == request.Subject, ct))
            {
                db.AccessTeamMembers.Add(new AccessTeamMember { TeamId = teamId, Subject = request.Subject });
                ResourceAccessAuditWriter.Record(db, http, access, "member.added", "member", request.Subject, teamId, null, new { teamId, request.Subject, username });
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        group.MapPut("/teams/{teamId:guid}/members/batch", async (Guid teamId, TeamMembershipBatchRequest request,
            DbScannerDbContext db, IKeycloakAdminService identity, IOptions<TenantIsolationOptions> tenancy,
            HttpContext http, ResourceAccessContext access, CancellationToken ct) =>
        {
            const int maximumBatchSize = 50;
            if (request.Subjects is null || request.Subjects.Count is < 1 or > maximumBatchSize
                || request.Subjects.Any(subject => string.IsNullOrWhiteSpace(subject) || subject.Length > 256 || subject != subject.Trim()))
                return Results.BadRequest(new { message = $"1 ile {maximumBatchSize} arasında geçerli Keycloak kullanıcı ID'si gönderilmelidir." });
            if (tenancy.Value.DedicatedTenant) return IdentityScopeUnavailable();

            var subjects = request.Subjects.Select(subject => subject!).Distinct(StringComparer.Ordinal).ToArray();
            var team = await db.AccessTeams.SingleOrDefaultAsync(t => t.Id == teamId, ct);
            if (team is null) return Results.NotFound();
            if (team.IsArchived) return Results.Conflict(new { message = "Arşivlenmiş bir ekibe üye eklenemez." });

            var users = new List<(string Subject, string Username)>();
            try
            {
                // Validate every identity before making any database change so a failed batch cannot partially add members.
                foreach (var subject in subjects)
                {
                    var user = await identity.GetUserAsync(subject, ct);
                    if (user is null || user.Id != subject)
                        return Results.NotFound(new { code = "IDENTITY_USER_NOT_FOUND", message = "Kullanıcı Keycloak'ta bulunamadı. Kullanıcıları listeden yeniden seçin." });
                    if (!user.Enabled)
                        return Results.Conflict(new { code = "IDENTITY_USER_DISABLED", message = "Devre dışı bir kullanıcı ekibe eklenemez." });
                    users.Add((subject, user.Username));
                }
            }
            catch (Exception error) when (error is IdentityManagementUnavailableException or InvalidOperationException)
            { return IdentityUnavailable(); }

            var existingSubjects = await db.AccessTeamMembers.AsNoTracking()
                .Where(member => member.TeamId == teamId && subjects.Contains(member.Subject))
                .Select(member => member.Subject).ToListAsync(ct);
            var existing = existingSubjects.ToHashSet(StringComparer.Ordinal);
            var addedCount = 0;
            foreach (var user in users)
            {
                if (existing.Contains(user.Subject)) continue;
                db.AccessTeamMembers.Add(new AccessTeamMember { TeamId = teamId, Subject = user.Subject });
                ResourceAccessAuditWriter.Record(db, http, access, "member.added", "member", user.Subject, teamId,
                    null, new { teamId, subject = user.Subject, username = user.Username });
                addedCount++;
            }

            // Member rows and their audit records are persisted together by one SaveChanges call.
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { addedCount, alreadyMemberCount = subjects.Length - addedCount });
        });
        group.MapDelete("/teams/{teamId:guid}/members/{subject}", async (Guid teamId, string subject, DbScannerDbContext db,
            HttpContext http, ResourceAccessContext access, CancellationToken ct) =>
        {
            var member = await db.AccessTeamMembers.SingleOrDefaultAsync(m => m.TeamId == teamId && m.Subject == subject, ct);
            if (member is null) return Results.NoContent();
            db.AccessTeamMembers.Remove(member);
            ResourceAccessAuditWriter.Record(db, http, access, "member.removed", "member", subject, teamId, new { teamId, subject }, null);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        group.MapPut("/grants", async (AccessGrantRequest request, DbScannerDbContext db,
            HttpContext http, ResourceAccessContext access, CancellationToken ct) =>
        {
            var permissions = request.Permissions;
            if (permissions == TargetPermission.None || (permissions & ~TargetPermission.All) != 0
                || !permissions.HasFlag(TargetPermission.Read))
                return Results.BadRequest(new { message = "İzinler Read içermelidir; geçerli değerler Read=1, Scan=2, Manage=4, Export=8." });
            var team = await db.AccessTeams.SingleOrDefaultAsync(t => t.Id == request.TeamId, ct);
            if (team?.IsArchived == true) return Results.Conflict(new { message = "Arşivlenmiş bir ekibe izin verilemez." });
            if (team is null
                || !await db.AccessProjects.AnyAsync(p => p.Id == request.ProjectId, ct)
                || (request.TargetId.HasValue && !await db.Targets.AnyAsync(t => t.Id == request.TargetId && t.ProjectId == request.ProjectId, ct)))
                return Results.NotFound();
            var grant = await db.AccessGrants.SingleOrDefaultAsync(g => g.TeamId == request.TeamId
                && g.ProjectId == request.ProjectId && g.TargetId == request.TargetId, ct);
            if (grant?.Permissions == permissions) return Results.Ok(grant);
            var before = grant is null ? null : ResourceAccessAuditWriter.GrantSnapshot(grant);
            if (grant is null)
            {
                grant = new AccessGrant { TeamId = request.TeamId, ProjectId = request.ProjectId, TargetId = request.TargetId };
                db.AccessGrants.Add(grant);
            }
            grant.Permissions = permissions;
            ResourceAccessAuditWriter.Record(db, http, access, before is null ? "grant.created" : "grant.updated", "grant", grant.Id.ToString(), grant.TeamId, before, ResourceAccessAuditWriter.GrantSnapshot(grant));
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
            { return ProjectReferenceChanged(); }
            return Results.Ok(grant);
        });
        group.MapDelete("/grants/{id:guid}", async (Guid id, DbScannerDbContext db,
            HttpContext http, ResourceAccessContext access, CancellationToken ct) =>
        {
            var grant = await db.AccessGrants.SingleOrDefaultAsync(g => g.Id == id, ct);
            if (grant is null) return Results.NoContent();
            ResourceAccessAuditWriter.Record(db, http, access, "grant.revoked", "grant", grant.Id.ToString(), grant.TeamId, ResourceAccessAuditWriter.GrantSnapshot(grant), null);
            db.AccessGrants.Remove(grant);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        group.MapPut("/targets/{id:guid}/project", async (Guid id, TargetProjectRequest request, DbScannerDbContext db,
            HttpContext http, ResourceAccessContext access, CancellationToken ct) =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var target = await db.Targets.FromSqlInterpolated($"SELECT * FROM targets WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct);
            if (target is null || !await db.AccessProjects.AnyAsync(p => p.Id == request.ProjectId, ct)) return Results.NotFound();
            if (target.ProjectId == request.ProjectId) return Results.NoContent();
            if (await db.Scans.AnyAsync(s => s.TargetId == id && (s.Status == ScanStatus.Pending || s.Status == ScanStatus.Scanning), ct))
                return Results.Conflict(new { message = "Aktif taraması olan bir hedef başka bir projeye taşınamaz." });
            // ProjectId is an alternate key: explicitly update after removing target-specific old grants.
            var removedGrants = await db.AccessGrants.AsNoTracking().Where(g => g.TargetId == id).ToListAsync(ct);
            foreach (var grant in removedGrants)
                ResourceAccessAuditWriter.Record(db, http, access, "grant.revoked", "grant", grant.Id.ToString(), grant.TeamId,
                    ResourceAccessAuditWriter.GrantSnapshot(grant), new { reason = "target.project_changed" });
            ResourceAccessAuditWriter.Record(db, http, access, "target.project_changed", "target", id.ToString(), null,
                new { target.Id, target.Name, target.ProjectId }, new { target.Id, target.Name, request.ProjectId });
            try
            {
                await db.AccessGrants.Where(g => g.TargetId == id).ExecuteDeleteAsync(ct);
                await db.Targets.Where(t => t.Id == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.ProjectId, request.ProjectId), ct);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.ForeignKeyViolation)
            {
                await transaction.RollbackAsync(ct);
                return ProjectReferenceChanged();
            }
            catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
            {
                await transaction.RollbackAsync(ct);
                return ProjectReferenceChanged();
            }
            return Results.NoContent();
        });
    }

    private static bool ValidName(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 160;

    private static IResult ProjectReferenceChanged() => Results.Conflict(new { code = "PROJECT_CONCURRENT_CHANGE",
        message = "Proje, ekip veya hedef başka bir işlem tarafından değiştirildi. Sayfayı yenileyip tekrar deneyin." });

    private static IResult IdentityUnavailable() => Results.Json(new { code = "IDENTITY_SERVICE_UNAVAILABLE",
        message = "Keycloak kullanıcıları alınamadı. Kimlik servisi bağlantısını ve servis hesabı yetkilerini kontrol edin." }, statusCode: 503);

    private static IResult IdentityScopeUnavailable() => Results.Json(new { code = "TENANT_IDENTITY_DIRECTORY_UNAVAILABLE",
        message = "Bu kurulumda kullanıcı arama ve ekip üyeliği yönetimi için tenant kapsamlı bir kimlik dizini gereklidir." }, statusCode: 403);
}
