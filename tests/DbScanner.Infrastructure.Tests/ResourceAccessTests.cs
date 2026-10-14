using System.Security.Claims;
using DbScanner.Application.Models;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Identity;
using DbScanner.Infrastructure.Messaging;
using DbScanner.Infrastructure.Persistence;
using DbScanner.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

public sealed class ResourceAccessTests
{
    [Fact]
    public async Task Archived_team_stops_target_access_but_keeps_members_and_grants()
    {
        var options = Options();
        var ids = await Seed(options);
        await using var user = new DbScannerDbContext(options, User("alice"));
        Assert.Single(await user.Targets.AsNoTracking().ToListAsync());
        await using (var system = new DbScannerDbContext(options, ResourceAccessContext.ForSystem()))
        {
            var team = await system.AccessTeams.SingleAsync(t => t.Id == ids.Team);
            team.IsArchived = true;
            await system.SaveChangesAsync();
            Assert.Single(await system.AccessTeamMembers.ToListAsync());
            Assert.Single(await system.AccessGrants.ToListAsync());
            Assert.Empty(await user.Targets.AsNoTracking().ToListAsync());
            team.IsArchived = false;
            await system.SaveChangesAsync();
        }
        Assert.Single(await user.Targets.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Audit_is_admin_only_and_append_only_even_for_system_context()
    {
        var options = Options();
        await using var system = new DbScannerDbContext(options, ResourceAccessContext.ForSystem());
        var audit = new ResourceAccessAudit { ActorSubject = "admin", Action = "team.created", EntityType = "team", EntityId = "test" };
        system.ResourceAccessAudits.Add(audit);
        await system.SaveChangesAsync();
        await using var user = new DbScannerDbContext(options, User("alice"));
        Assert.Empty(await user.ResourceAccessAudits.ToListAsync());
        audit.Action = "tampered";
        await Assert.ThrowsAsync<InvalidOperationException>(() => system.SaveChangesAsync());
        system.ChangeTracker.Clear();
        system.ResourceAccessAudits.Remove(await system.ResourceAccessAudits.SingleAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => system.SaveChangesAsync());
    }

    [Fact]
    public async Task Default_context_denies_targets_scans_results_and_reports()
    {
        var options = Options();
        var ids = await Seed(options);
        await using var db = new DbScannerDbContext(options, new ResourceAccessContext());
        Assert.Empty(await db.Targets.ToListAsync());
        Assert.Empty(await db.Scans.ToListAsync());
        Assert.Empty(await db.ScanScriptResults.ToListAsync());
        Assert.Empty(await new FindingRepository(db).ListAllAsync());
        Assert.Empty(await db.ScanOutboxMessages.ToListAsync());
        Assert.Null(await new ScanRepository(db).GetByIdAsync(ids.ScanA));
    }

    [Fact]
    public async Task Project_grant_filters_lists_nested_results_and_direct_ID_lookups()
    {
        var options = Options();
        var ids = await Seed(options);
        await using var db = new DbScannerDbContext(options, User("alice"));
        Assert.Equal(ids.TargetA, (await db.Targets.SingleAsync()).Id);
        var scan = await new ScanRepository(db).GetByIdAsync(ids.ScanA);
        Assert.NotNull(scan);
        Assert.Single(scan.ScriptResults);
        Assert.Null(await new ScanRepository(db).GetByIdAsync(ids.ScanB));
        Assert.Null(await new TargetRepository(db).GetByIdAsync(ids.TargetB));
        Assert.Equal(ids.TargetA, (await new FindingRepository(db).ListAllAsync()).Single().TargetId);
    }

    [Fact]
    public async Task Export_scan_and_manage_require_separate_grants_and_role_ceiling()
    {
        var options = Options();
        var ids = await Seed(options);
        var user = User("alice");
        await using var db = new DbScannerDbContext(options, user);
        user.RequiredPermission = TargetPermission.Read | TargetPermission.Export;
        Assert.Empty(await new FindingRepository(db).ListAllAsync());
        user.RequiredPermission = TargetPermission.Read | TargetPermission.Manage;
        Assert.Empty(await db.Targets.ToListAsync());
        user.RequiredPermission = TargetPermission.Read | TargetPermission.Scan;
        Assert.Single(await db.Targets.ToListAsync());
        user.RolePermissions = TargetPermission.Read | TargetPermission.Export; // viewer with Scan grant
        Assert.Empty(await db.Targets.ToListAsync());
        user.RequiredPermission = TargetPermission.Read;
        Assert.Single(await db.Targets.ToListAsync());
        Assert.Null(await db.Targets.SingleOrDefaultAsync(t => t.Id == ids.TargetB));
    }

    [Fact]
    public async Task Membership_revocation_is_effective_without_waiting_for_token_expiry()
    {
        var options = Options();
        await Seed(options);
        await using var userDb = new DbScannerDbContext(options, User("alice"));
        Assert.Single(await userDb.Targets.AsNoTracking().ToListAsync());
        await using (var admin = new DbScannerDbContext(options, ResourceAccessContext.ForSystem()))
        {
            admin.AccessTeamMembers.Remove(await admin.AccessTeamMembers.SingleAsync(m => m.Subject == "alice"));
            await admin.SaveChangesAsync();
        }
        Assert.Empty(await userDb.Targets.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Target_only_grant_does_not_expose_other_targets_in_the_same_project()
    {
        var options = Options();
        var ids = await Seed(options);
        await using (var admin = new DbScannerDbContext(options, ResourceAccessContext.ForSystem()))
        {
            var grant = await admin.AccessGrants.SingleAsync();
            grant.TargetId = ids.TargetA;
            admin.Targets.Add(Target("same-project-hidden", ids.ProjectA));
            await admin.SaveChangesAsync();
        }
        await using var userDb = new DbScannerDbContext(options, User("alice"));
        Assert.Single(await userDb.Targets.ToListAsync());
    }

    [Fact]
    public async Task Read_only_principal_cannot_attach_and_modify_a_target_or_create_a_scan()
    {
        var options = Options();
        var ids = await Seed(options);
        await using var db = new DbScannerDbContext(options, User("alice"));
        var target = await db.Targets.SingleAsync();
        target.Name = "unauthorized-edit";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Scans.Add(new Scan { TargetId = ids.TargetA });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.AccessTeamMembers.Add(new AccessTeamMember { TeamId = ids.Team, Subject = "mallory" });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Administrator_access_is_not_frozen_into_the_cached_EF_model()
    {
        var options = Options();
        await Seed(options);
        await using (var admin = new DbScannerDbContext(options, new ResourceAccessContext { IsAdministrator = true }))
            Assert.Equal(2, await admin.Targets.CountAsync());
        await using (var other = new DbScannerDbContext(options, User("mallory")))
            Assert.Empty(await other.Targets.ToListAsync());
        await using (var member = new DbScannerDbContext(options, User("alice")))
            Assert.Single(await member.Targets.ToListAsync());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("customer-b", false)]
    [InlineData("customer-a", true)]
    public void Dedicated_tenant_requires_an_exact_authenticated_claim(string? claim, bool allowed)
    {
        var config = new TenantIsolationOptions { TenantId = "customer-a", DedicatedTenant = true };
        var claims = new List<Claim> { new("sub", "alice"), new("roles", "dbscanner-admin") };
        if (claim is not null) claims.Add(new Claim("dbscanner_tenant", claim));
        Assert.Equal(allowed, config.Allows(new ClaimsPrincipal(new ClaimsIdentity(claims, "trusted", null, "roles"))));
        Assert.False(config.Allows(new ClaimsPrincipal(new ClaimsIdentity(claims))));
        Assert.Equal(allowed, config.AcceptsMessage(claim));
    }

    [Fact]
    public void Ambiguous_claims_are_denied_and_all_queue_types_are_namespaced()
    {
        var config = new TenantIsolationOptions { TenantId = "customer-a", DedicatedTenant = true };
        Assert.False(config.Allows(new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("dbscanner_tenant", "customer-a"), new Claim("dbscanner_tenant", "customer-b")], "trusted"))));
        var queues = new RabbitMqOptions();
        config.NamespaceQueues(queues);
        Assert.All(new[] { queues.RequestedQueue, queues.RetryQueue, queues.DeadLetterQueue,
            queues.DiscoveryRequestedQueue, queues.DiscoveryDeadLetterQueue, queues.NseScriptSyncQueue },
            q => Assert.StartsWith("tenant.customer-a.", q));
        Assert.False(new TenantIsolationOptions { TenantId = "../bad", DedicatedTenant = true }.IsValid());
        Assert.False(new TenantIsolationOptions { DedicatedTenant = true }.IsValid());
        Assert.True(new TenantIsolationOptions().AcceptsMessage(null));
        Assert.False(new TenantIsolationOptions().AcceptsMessage("customer-b"));
    }

    internal static ResourceAccessContext User(string subject) => new() { Subject = subject, RolePermissions = TargetPermission.All };
    internal static Target Target(string name, Guid project) => new() { Name = name, Host = name + ".internal",
        NormalizedHost = name + ".internal", Port = 5432, Engine = DatabaseEngine.PostgreSql, ProjectId = project };
    private static DbContextOptions<DbScannerDbContext> Options() => new DbContextOptionsBuilder<DbScannerDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    private static async Task<(Guid TargetA, Guid TargetB, Guid ScanA, Guid ScanB, Guid ProjectA, Guid Team)> Seed(DbContextOptions<DbScannerDbContext> options)
    {
        await using var db = new DbScannerDbContext(options, ResourceAccessContext.ForSystem());
        var projectA = new AccessProject { Name = "A" };
        var projectB = new AccessProject { Name = "B" };
        var team = new AccessTeam { Name = "Security A" };
        var a = Target("a", projectA.Id); var b = Target("b", projectB.Id);
        var sa = new Scan { TargetId = a.Id }; var sb = new Scan { TargetId = b.Id };
        db.AddRange(projectA, projectB, team, a, b, sa, sb,
            new AccessTeamMember { TeamId = team.Id, Subject = "alice" },
            new AccessGrant { TeamId = team.Id, ProjectId = projectA.Id, Permissions = TargetPermission.Read | TargetPermission.Scan });
        foreach (var (target, scan) in new[] { (a, sa), (b, sb) })
        {
            db.ScanScriptResults.Add(new ScanScriptResult { ScanId = scan.Id, ScriptName = "banner" });
            db.Findings.Add(new Finding { ScanId = scan.Id, TargetId = target.Id, RuleId = "test", Title = "secret-" + target.Name,
                Evidence = "private-" + target.Name, Remediation = "fix", SourceScript = "banner" });
        }
        await db.SaveChangesAsync();
        return (a.Id, b.Id, sa.Id, sb.Id, projectA.Id, team.Id);
    }
}
