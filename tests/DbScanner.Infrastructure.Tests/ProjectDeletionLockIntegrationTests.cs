using DbScanner.Application.Models;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Persistence;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

// The project -> grant FK is intentionally still CASCADE. These real PostgreSQL
// regressions verify the parent-row locking protocol used by the API, in both
// possible insertion/deletion orderings. They do not touch the running AppHost.
[Collection(DatabaseContainerTestCollection.Name)]
public sealed class ProjectDeletionLockIntegrationTests(ProjectDeletionPostgresFixture fixture)
    : IClassFixture<ProjectDeletionPostgresFixture>
{
    [DockerFact]
    public async Task Project_delete_lock_blocks_new_grants_and_targets_until_they_fail_without_cascading()
    {
        foreach (var insertTarget in new[] { false, true })
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var ct = timeout.Token;
            await using var deletion = fixture.Context();
            var project = new AccessProject { Name = "Delete before insert" };
            var team = new AccessTeam { Name = "Concurrent team" };
            deletion.AddRange(project, team);
            await deletion.SaveChangesAsync(ct);

            await using var transaction = await deletion.Database.BeginTransactionAsync(ct);
            var locked = await deletion.AccessProjects.FromSqlInterpolated(
                $"SELECT * FROM access_projects WHERE \"Id\" = {project.Id} FOR UPDATE").SingleAsync(ct);
            Assert.False(await deletion.Targets.AnyAsync(t => t.ProjectId == project.Id, ct));
            Assert.False(await deletion.AccessGrants.AnyAsync(g => g.ProjectId == project.Id, ct));

            var insertionName = "project-insert-" + Guid.NewGuid().ToString("N");
            await using var insertion = fixture.Context(insertionName);
            if (insertTarget) insertion.Targets.Add(new Target { Name = "Concurrent target", ProjectId = project.Id,
                Host = "127.0.0.1", Port = 5432, Engine = DatabaseEngine.PostgreSql });
            else insertion.AccessGrants.Add(new AccessGrant { TeamId = team.Id, ProjectId = project.Id, Permissions = TargetPermission.Read });
            var insert = insertion.SaveChangesAsync(ct);
            await fixture.WaitForLockAsync(insertionName, ct);
            Assert.False(insert.IsCompleted);

            deletion.ResourceAccessAudits.Add(new ResourceAccessAudit { ActorSubject = "test-admin", Action = "project.deleted",
                EntityType = "project", EntityId = project.Id.ToString(), BeforeJson = "{}" });
            deletion.AccessProjects.Remove(locked);
            await deletion.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => insert);
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);

            await using var verification = fixture.Context();
            Assert.False(await verification.AccessProjects.AnyAsync(p => p.Id == project.Id, ct));
            Assert.False(await verification.AccessGrants.AnyAsync(g => g.ProjectId == project.Id, ct));
            Assert.False(await verification.Targets.AnyAsync(t => t.ProjectId == project.Id, ct));
            Assert.Equal(1, await verification.ResourceAccessAudits.CountAsync(a => a.EntityId == project.Id.ToString(), ct));
        }
    }

    [DockerFact]
    public async Task Already_pending_grant_insert_delays_delete_lock_and_is_visible_before_dependency_check()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ct = timeout.Token;
        await using var seed = fixture.Context();
        var project = new AccessProject { Name = "Insert before delete" };
        var team = new AccessTeam { Name = "Committed grant team" };
        seed.AddRange(project, team);
        await seed.SaveChangesAsync(ct);

        await using var insertion = fixture.Context();
        await using var insertTransaction = await insertion.Database.BeginTransactionAsync(ct);
        var grant = new AccessGrant { TeamId = team.Id, ProjectId = project.Id, Permissions = TargetPermission.Read };
        insertion.AccessGrants.Add(grant);
        await insertion.SaveChangesAsync(ct);

        var deletionName = "project-delete-" + Guid.NewGuid().ToString("N");
        await using var deletion = fixture.Context(deletionName);
        await using var deleteTransaction = await deletion.Database.BeginTransactionAsync(ct);
        var acquiringLock = deletion.AccessProjects.FromSqlInterpolated(
            $"SELECT * FROM access_projects WHERE \"Id\" = {project.Id} FOR UPDATE").SingleAsync(ct);
        await fixture.WaitForLockAsync(deletionName, ct);
        Assert.False(acquiringLock.IsCompleted);
        await insertTransaction.CommitAsync(ct);
        await acquiringLock;

        // The API now returns PROJECT_HAS_GRANTS and rolls back without deleting.
        Assert.True(await deletion.AccessGrants.AnyAsync(g => g.ProjectId == project.Id, ct));
        await deleteTransaction.RollbackAsync(ct);
        await using var verification = fixture.Context();
        Assert.True(await verification.AccessProjects.AnyAsync(p => p.Id == project.Id, ct));
        Assert.True(await verification.AccessGrants.AnyAsync(g => g.Id == grant.Id, ct));
        Assert.Empty(await verification.ResourceAccessAudits.Where(a => a.EntityId == project.Id.ToString()).ToListAsync(ct));
    }
}

public sealed class ProjectDeletionPostgresFixture : IAsyncLifetime
{
    private IContainer? postgres;
    private string? connectionString;

    public async Task InitializeAsync()
    {
        if (!DockerFactAttribute.CanRun("docker", "version --format {{.Server.Version}}")) return;
        var password = Guid.NewGuid().ToString("N");
        postgres = new ContainerBuilder("postgres:17.6")
            .WithEnvironment("POSTGRES_PASSWORD", password).WithPortBinding(5432, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432)).Build();
        await postgres.StartAsync();
        connectionString = new NpgsqlConnectionStringBuilder { Host = postgres.Hostname,
            Port = postgres.GetMappedPublicPort(5432), Username = "postgres", Password = password, Database = "postgres" }.ConnectionString;
        await using var db = Context();
        await db.Database.MigrateAsync();
    }

    internal DbScannerDbContext Context(string? applicationName = null)
    {
        var connection = new NpgsqlConnectionStringBuilder(connectionString!) { ApplicationName = applicationName ?? "project-lock-tests" };
        return new DbScannerDbContext(new DbContextOptionsBuilder<DbScannerDbContext>().UseNpgsql(connection.ConnectionString).Options,
            new ResourceAccessContext { IsSystem = true });
    }

    internal async Task WaitForLockAsync(string applicationName, CancellationToken ct)
    {
        await using var observer = new NpgsqlConnection(connectionString);
        await observer.OpenAsync(ct);
        await using var query = observer.CreateCommand();
        query.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = @name AND wait_event_type = 'Lock')";
        query.Parameters.AddWithValue("name", applicationName);
        while (!(bool)(await query.ExecuteScalarAsync(ct))!) await Task.Delay(25, ct);
    }

    public async Task DisposeAsync()
    {
        if (postgres is not null) await postgres.DisposeAsync();
    }
}
