using System.Data;
using DbScanner.Application.Models;
using DbScanner.Domain.Entities;
using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DbScanner.Api.Security;

public sealed record AccessProjectSummary(Guid Id, string Name, bool IsDefault);

public static class ResourceAccessProjectEndpoints
{
    public static void MapResourceAccessProjects(this RouteGroupBuilder group)
    {
        group.MapGet("/projects/{id:guid}", async (Guid id, DbScannerDbContext db, CancellationToken ct) =>
        {
            var project = await db.AccessProjects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
            if (project is null) return Results.NotFound(new { message = "Proje bulunamadı." });
            return Results.Ok(new
            {
                project = Summary(project),
                targets = await db.Targets.AsNoTracking().Where(t => t.ProjectId == id).OrderBy(t => t.Name)
                    .Select(t => new { t.Id, t.Name, t.ProjectId }).ToListAsync(ct),
                grants = await db.AccessGrants.AsNoTracking().Where(g => g.ProjectId == id).ToListAsync(ct),
                teams = await db.AccessTeams.AsNoTracking().OrderBy(t => t.Name)
                    .Select(t => new { t.Id, t.Name, t.IsArchived }).ToListAsync(ct)
            });
        });

        group.MapPut("/projects/{id:guid}", async (Guid id, AccessNameRequest request, DbScannerDbContext db,
            HttpContext http, ResourceAccessContext access, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160)
                return Results.BadRequest(new { message = "Proje adı 1 ile 160 karakter arasında olmalıdır." });
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : null;
            var project = await FindForUpdateAsync(db, id, ct);
            if (project is null) return Results.NotFound(new { message = "Proje bulunamadı." });
            var name = request.Name.Trim();
            if (project.Name == name) return Results.Ok(Summary(project));
            var before = Summary(project);
            project.Name = name;
            ResourceAccessAuditWriter.Record(db, http, access, "project.renamed", "project", id.ToString(), null, before, Summary(project));
            try
            {
                await db.SaveChangesAsync(ct);
                if (transaction is not null) await transaction.CommitAsync(ct);
            }
            catch (DbUpdateConcurrencyException) { return ConcurrentChange(); }
            return Results.Ok(Summary(project));
        });

        group.MapDelete("/projects/{id:guid}", async (Guid id, DbScannerDbContext db,
            HttpContext http, ResourceAccessContext access, CancellationToken ct) =>
        {
            if (id == AccessProject.DefaultId)
                return Results.Conflict(new { code = "PROJECT_DEFAULT", message = "Varsayılan proje silinemez. Adını düzenleyebilirsiniz." });

            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct) : null;
            // PostgreSQL FK checks acquire KEY SHARE on this parent row. FOR UPDATE blocks
            // new target/grant references until our dependency checks and delete commit.
            // This is essential because the existing project -> grant FK uses CASCADE.
            var project = await FindForUpdateAsync(db, id, ct);
            if (project is null) return Results.NoContent();
            if (await db.Targets.AnyAsync(t => t.ProjectId == id, ct)) return HasTargets();
            if (await db.AccessGrants.AnyAsync(g => g.ProjectId == id, ct)) return HasGrants();

            ResourceAccessAuditWriter.Record(db, http, access, "project.deleted", "project", id.ToString(), null, Summary(project), null);
            db.AccessProjects.Remove(project);
            try
            {
                // Audit and removal are committed together; never delete targets or scans.
                await db.SaveChangesAsync(ct);
                if (transaction is not null) await transaction.CommitAsync(ct);
            }
            catch (DbUpdateConcurrencyException) { return ConcurrentChange(); }
            catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation })
            {
                // Defense in depth for a referencing row: no provider SQL/details leak.
                if (transaction is not null) await transaction.RollbackAsync(ct);
                return HasTargets();
            }
            return Results.NoContent();
        });
    }

    private static Task<AccessProject?> FindForUpdateAsync(DbScannerDbContext db, Guid id, CancellationToken ct) =>
        db.Database.IsNpgsql()
            ? db.AccessProjects.FromSqlInterpolated($"SELECT * FROM access_projects WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct)
            : db.AccessProjects.SingleOrDefaultAsync(p => p.Id == id, ct);

    private static AccessProjectSummary Summary(AccessProject project) => new(project.Id, project.Name, project.Id == AccessProject.DefaultId);
    private static IResult HasTargets() => Results.Conflict(new { code = "PROJECT_HAS_TARGETS", message = "Projede hedefler var. Silmeden önce hedefleri başka bir projeye taşıyın." });
    private static IResult HasGrants() => Results.Conflict(new { code = "PROJECT_HAS_GRANTS", message = "Projeye bağlı ekip izinleri var. Silmeden önce bu izinleri kaldırın." });
    private static IResult ConcurrentChange() => Results.Conflict(new { code = "PROJECT_CONCURRENT_CHANGE", message = "Proje başka bir işlem tarafından değiştirildi. Sayfayı yenileyip tekrar deneyin." });
}
