using System.Text.Json;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Identity;
using DbScanner.Infrastructure.Messaging;
using DbScanner.Infrastructure.Persistence;
using DbScanner.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

[Collection(DatabaseContainerTestCollection.Name)]
public sealed class ResourceAccessIntegrationTests(ScanReliabilityFixture fixture) : IClassFixture<ScanReliabilityFixture>
{
    [DockerFact]
    public async Task PostgreSQL_audit_and_change_are_atomic_and_audit_rejects_SQL_mutation()
    {
        await using var environment = await fixture.CreateEnvironmentAsync();
        using var scope = environment.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbScannerDbContext>();
        var team = new AccessTeam { Name = "Must roll back" };
        db.AddRange(team, new ResourceAccessAudit { ActorSubject = new string('x', 257), Action = "team.created", EntityType = "team", EntityId = team.Id.ToString() });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.False(await db.AccessTeams.AnyAsync(t => t.Id == team.Id));
        Assert.Empty(await db.ResourceAccessAudits.ToListAsync());
        db.ResourceAccessAudits.Add(new ResourceAccessAudit { ActorSubject = "admin", Action = "team.created", EntityType = "team", EntityId = "test" });
        await db.SaveChangesAsync();
        foreach (var sql in new[] { "UPDATE resource_access_audits SET \"Action\" = 'tampered'", "DELETE FROM resource_access_audits", "TRUNCATE resource_access_audits" })
        {
            var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
            Assert.Equal("55000", error.SqlState);
        }
        Assert.Equal("team.created", (await db.ResourceAccessAudits.AsNoTracking().SingleAsync()).Action);
    }

    [DockerFact]
    public async Task PostgreSQL_archive_revokes_access_without_erasing_grants_or_history()
    {
        await using var environment = await fixture.CreateEnvironmentAsync();
        using var scope = environment.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbScannerDbContext>();
        var target = await environment.AddTargetAsync(db);
        var team = new AccessTeam { Name = "Archived team" };
        db.AddRange(team, new AccessTeamMember { TeamId = team.Id, Subject = "alice" },
            new AccessGrant { TeamId = team.Id, ProjectId = target.ProjectId, TargetId = target.Id, Permissions = TargetPermission.All });
        await db.SaveChangesAsync();
        await using var user = new DbScannerDbContext(db.GetOptions(), ResourceAccessTests.User("alice"));
        Assert.True(await user.Targets.AnyAsync(t => t.Id == target.Id));
        team.IsArchived = true;
        await db.SaveChangesAsync();
        Assert.False(await user.Targets.AnyAsync(t => t.Id == target.Id));
        Assert.True(await db.AccessTeamMembers.AnyAsync(m => m.TeamId == team.Id));
        Assert.True(await db.AccessGrants.AnyAsync(g => g.TeamId == team.Id));
        Assert.True(await db.Targets.AnyAsync(t => t.Id == target.Id));
    }

    [DockerFact]
    public async Task Dedicated_tenant_with_non_superuser_and_private_vhost_completes_a_scan()
    {
        await using var environment = await fixture.CreateDedicatedEnvironmentAsync("customer-a");
        using (var scope = environment.Services.CreateScope())
            await new TenantDeploymentVerifier(scope.ServiceProvider).VerifyAsync();
        var request = await environment.StageAsync();
        Assert.Equal("customer-a", request.TenantId);
        Assert.StartsWith("tenant.customer-a.", environment.QueueOptions.RequestedQueue);
        Assert.True(await environment.Dispatcher().DispatchOnceAsync(CancellationToken.None));
        using var stop = new CancellationTokenSource();
        var loop = environment.Services.GetRequiredService<IScanJobConsumer>().StartAsync(stop.Token);
        try
        {
            await environment.WaitForCompletedAsync(request.ScanId);
        }
        finally
        {
            await stop.CancelAsync();
            await loop;
        }
        Assert.Equal(1, environment.Recorder.Completed);
    }

    [DockerFact]
    public async Task PostgreSQL_filters_lists_details_reports_and_bulk_cancellation()
    {
        await using var environment = await fixture.CreateEnvironmentAsync();
        using var scope = environment.Services.CreateScope();
        var system = scope.ServiceProvider.GetRequiredService<DbScannerDbContext>();
        var project = new AccessProject { Name = "Allowed" }; var team = new AccessTeam { Name = "Readers" };
        var a = ResourceAccessTests.Target("allowed", project.Id);
        var b = ResourceAccessTests.Target("hidden", AccessProject.DefaultId);
        var sa = new Scan { TargetId = a.Id }; var sb = new Scan { TargetId = b.Id };
        var ruleId = await system.Rules.Select(r => r.Id).FirstAsync();
        system.AddRange(project, team, a, b, sa, sb,
            new AccessTeamMember { TeamId = team.Id, Subject = "alice" },
            new AccessGrant { TeamId = team.Id, ProjectId = project.Id, Permissions = TargetPermission.Read | TargetPermission.Scan });
        foreach (var (target, scan) in new[] { (a, sa), (b, sb) })
            system.Findings.Add(new Finding { TargetId = target.Id, ScanId = scan.Id, RuleId = ruleId, Title = "private",
                Evidence = "sensitive", Remediation = "fix", SourceScript = "banner" });
        await system.SaveChangesAsync();
        var access = ResourceAccessTests.User("alice");
        await using var user = new DbScannerDbContext(system.GetOptions(), access);
        Assert.Equal(a.Id, (await user.Targets.SingleAsync()).Id);
        Assert.Single(await new FindingRepository(user).ListAllAsync());
        Assert.Null(await new ScanRepository(user).GetByIdAsync(sb.Id));
        access.RequiredPermission = TargetPermission.Read | TargetPermission.Scan;
        await using (var transaction = await user.Database.BeginTransactionAsync())
        {
            Assert.False(await user.Targets.FromSqlInterpolated($"SELECT * FROM targets WHERE \"Id\" = {b.Id} FOR UPDATE").AnyAsync());
            Assert.True(await user.Targets.FromSqlInterpolated($"SELECT * FROM targets WHERE \"Id\" = {a.Id} FOR UPDATE").AnyAsync());
            await transaction.CommitAsync();
        }
        await new ScanRepository(user).UpdateStatusAsync(sb.Id, ScanStatus.Cancelled);
        system.ChangeTracker.Clear();
        Assert.Equal(ScanStatus.Pending, (await system.Scans.SingleAsync(s => s.Id == sb.Id)).Status);
        await new ScanRepository(user).UpdateStatusAsync(sa.Id, ScanStatus.Cancelled);
        system.ChangeTracker.Clear();
        Assert.Equal(ScanStatus.Cancelled, (await system.Scans.SingleAsync(s => s.Id == sa.Id)).Status);
        access.RequiredPermission = TargetPermission.Read | TargetPermission.Export;
        Assert.Empty(await new FindingRepository(user).ListAllAsync());
    }

    [DockerFact]
    public async Task Database_rejects_a_grant_pointing_to_a_target_in_another_project()
    {
        await using var environment = await fixture.CreateEnvironmentAsync();
        using var scope = environment.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbScannerDbContext>();
        var team = new AccessTeam { Name = "Security" }; var project = new AccessProject { Name = "Other" };
        var target = await environment.AddTargetAsync(db);
        db.AddRange(team, project);
        await db.SaveChangesAsync();
        db.AccessGrants.Add(new AccessGrant { TeamId = team.Id, ProjectId = project.Id, TargetId = target.Id, Permissions = TargetPermission.Read });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal("23503", ((Npgsql.PostgresException)error.InnerException!).SqlState);
    }

    [DockerFact]
    public async Task Database_binding_prevents_two_tenant_deployments_using_the_same_database()
    {
        await using var environment = await fixture.CreateEnvironmentAsync();
        using var scope = environment.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbScannerDbContext>();
        await new TenantDeploymentVerifier(scope.ServiceProvider).VerifyAsync();
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(Options.Create(new TenantIsolationOptions { TenantId = "other" }));
        using var conflicting = services.BuildServiceProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new TenantDeploymentVerifier(conflicting).VerifyBeforeMigrationAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => new TenantDeploymentVerifier(conflicting).VerifyAsync());
        Assert.Equal("local", (await db.Set<DeploymentTenant>().SingleAsync()).TenantId);
    }

    [DockerFact]
    public async Task Dedicated_deployment_rejects_a_superuser_database_login()
    {
        await using var environment = await fixture.CreateEnvironmentAsync();
        using var scope = environment.Services.CreateScope();
        var services = new ServiceCollection();
        services.AddSingleton(scope.ServiceProvider.GetRequiredService<DbScannerDbContext>());
        services.AddSingleton(Options.Create(new TenantIsolationOptions { TenantId = "customer-a", DedicatedTenant = true }));
        using var provider = services.BuildServiceProvider();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new TenantDeploymentVerifier(provider).VerifyAsync());
        Assert.Contains("non-superuser", error.Message);
    }

    [DockerFact]
    public async Task Separate_databases_isolate_even_identical_scan_and_target_IDs()
    {
        await using var a = await fixture.CreateEnvironmentAsync();
        await using var b = await fixture.CreateEnvironmentAsync();
        var request = await a.StageAsync();
        using var sa = a.Services.CreateScope(); using var sb = b.Services.CreateScope();
        var aDb = sa.ServiceProvider.GetRequiredService<DbScannerDbContext>();
        var bDb = sb.ServiceProvider.GetRequiredService<DbScannerDbContext>();
        var original = await aDb.Targets.SingleAsync(t => t.Id == request.TargetId);
        bDb.Targets.Add(new Target { Id = original.Id, Name = "Customer B", Host = "customer-b.internal",
            NormalizedHost = "customer-b.internal", Port = 5432, Engine = DatabaseEngine.PostgreSql });
        bDb.Scans.Add(new Scan { Id = request.ScanId, TargetId = original.Id });
        await bDb.SaveChangesAsync();
        Assert.Equal("Customer B", (await new ScanRepository(bDb).GetByIdAsync(request.ScanId))!.Target!.Name);
        Assert.NotEqual("Customer B", (await new ScanRepository(aDb).GetByIdAsync(request.ScanId))!.Target!.Name);
        Assert.Empty(await bDb.ScanOutboxMessages.ToListAsync());
    }

    [DockerFact]
    public async Task Wrong_tenant_message_is_dead_lettered_without_claiming_or_executing_the_scan()
    {
        await using var environment = await fixture.CreateEnvironmentAsync();
        var request = await environment.StageAsync();
        await using var connection = await environment.Services.GetRequiredService<RabbitMqConnectionFactory>().Create().CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await RabbitMqTopology.DeclareAsync(channel, environment.QueueOptions, CancellationToken.None);
        await channel.BasicPublishAsync("", environment.QueueOptions.RequestedQueue, true,
            new BasicProperties { Persistent = true }, JsonSerializer.SerializeToUtf8Bytes(request with { TenantId = "other" }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        using var stop = new CancellationTokenSource();
        var loop = environment.Services.GetRequiredService<IScanJobConsumer>().StartAsync(stop.Token);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        BasicGetResult? deadLetter;
        while ((deadLetter = await channel.BasicGetAsync(environment.QueueOptions.DeadLetterQueue, true, timeout.Token)) is null)
            await Task.Delay(50, timeout.Token);
        await stop.CancelAsync();
        await loop;
        Assert.Equal(0, environment.Recorder.Started);
        using var scope = environment.Services.CreateScope();
        var scan = await scope.ServiceProvider.GetRequiredService<DbScannerDbContext>().Scans.SingleAsync();
        Assert.Equal(ScanStatus.Pending, scan.Status);
        Assert.Null(scan.LeaseToken);
        Assert.Equal(0, scan.ExecutionAttempt);
    }

    [DockerFact]
    public async Task Migration_keeps_user_edited_demo_targets_and_their_scan_history()
    {
        await using var environment = await fixture.CreateEnvironmentAsync();
        using var scope = environment.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbScannerDbContext>();
        var target = await db.Targets.FirstAsync();
        target.Name = "User edited name";
        var scan = new Scan { TargetId = target.Id };
        db.Scans.Add(scan);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261006112320_AddScanOutboxAndExecutionLeases");
        await migrator.MigrateAsync();
        Assert.Equal("User edited name", (await db.Targets.SingleAsync(t => t.Id == target.Id)).Name);
        Assert.True(await db.Scans.AnyAsync(s => s.Id == scan.Id));
    }
}

internal static class ResourceAccessTestOptions
{
    internal static DbContextOptions<DbScannerDbContext> GetOptions(this DbScannerDbContext db) =>
        db.GetService<IDbContextOptions>() as DbContextOptions<DbScannerDbContext>
        ?? throw new InvalidOperationException("Context options missing.");
}
