using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Persistence;
using DbScanner.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

public sealed class DbScannerDbContextTests
{
    [Fact]
    public async Task EnsureCreated_LoadsSeedData()
    {
        var options = new DbContextOptionsBuilder<DbScannerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new DbScannerDbContext(options, DbScanner.Application.Models.ResourceAccessContext.ForSystem());
        await dbContext.Database.EnsureCreatedAsync();

        Assert.True(await dbContext.Targets.AnyAsync());
        Assert.True(await dbContext.Rules.AnyAsync());
        Assert.True(await dbContext.NseScripts.AnyAsync());
        Assert.True(await dbContext.ScanScopeAllowlist.AnyAsync());
    }

    [Fact]
    public async Task UserManagementAuditRepository_PersistsSanitizedAuditFields()
    {
        var options = new DbContextOptionsBuilder<DbScannerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new DbScannerDbContext(options, DbScanner.Application.Models.ResourceAccessContext.ForSystem());
        var repository = new UserManagementAuditRepository(dbContext);
        var audit = new UserManagementAudit
        {
            ActorSubject = "actor-id",
            ActorUsername = "admin",
            ActorEmail = "admin@example.com",
            Action = "USER_ROLE_CHANGED",
            TargetUserId = "target-id",
            PreviousRole = "dbscanner-viewer",
            NewRole = "dbscanner-operator",
            PreviousEnabled = true,
            NewEnabled = true,
            Succeeded = true
        };

        await repository.AddAsync(audit);
        await repository.SaveChangesAsync();

        var persisted = await dbContext.UserManagementAudits.SingleAsync();
        Assert.Equal("actor-id", persisted.ActorSubject);
        Assert.Equal("target-id", persisted.TargetUserId);
        Assert.Equal("dbscanner-operator", persisted.NewRole);
        Assert.Null(persisted.FailureCode);
    }

    [Fact]
    public void ModelSnapshot_IsPresentForMigrations()
    {
        var migrationAssembly = typeof(DbScannerDbContext).Assembly;

        Assert.NotNull(migrationAssembly.GetType("DbScanner.Infrastructure.Persistence.Migrations.InitialCreate"));
        Assert.NotNull(migrationAssembly.GetType("DbScanner.Infrastructure.Persistence.Migrations.DbScannerDbContextModelSnapshot"));
    }

    [Fact]
    public async Task ListRunnableByIdsAsync_AllowsExplicitCrossEngineSelection()
    {
        var options = new DbContextOptionsBuilder<DbScannerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new DbScannerDbContext(options, DbScanner.Application.Models.ResourceAccessContext.ForSystem());
        dbContext.NseScripts.Add(new NseScript
        {
            Id = "mysql-info-explicit",
            NseScriptName = "mysql-info-explicit",
            DisplayName = "MySQL Info Explicit",
            Description = "Explicitly selected MySQL script.",
            SupportedEngines = "mysql",
            ApprovalStatus = ScriptApprovalStatus.Approved
        });
        await dbContext.SaveChangesAsync();

        var repository = new NseScriptRepository(dbContext);
        var scripts = await repository.ListRunnableByIdsAsync(
            DatabaseEngine.PostgreSql,
            ["mysql-info-explicit"]);

        Assert.Single(scripts);
        Assert.Equal("mysql-info-explicit", scripts[0].Id);
    }

    [Fact]
    public async Task ListRunnableByIdsAsync_RejectsExplicitDisabledSelection()
    {
        var options = new DbContextOptionsBuilder<DbScannerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new DbScannerDbContext(options, DbScanner.Application.Models.ResourceAccessContext.ForSystem());
        dbContext.NseScripts.Add(new NseScript
        {
            Id = "disabled-explicit",
            NseScriptName = "disabled-explicit",
            DisplayName = "Disabled Explicit",
            Description = "Disabled scripts must not run even when explicitly requested.",
            SupportedEngines = "generic",
            ApprovalStatus = ScriptApprovalStatus.Disabled
        });
        await dbContext.SaveChangesAsync();

        var repository = new NseScriptRepository(dbContext);
        var scripts = await repository.ListRunnableByIdsAsync(
            DatabaseEngine.PostgreSql,
            ["disabled-explicit"]);

        Assert.Empty(scripts);
    }

    [Fact]
    public async Task FindingRepository_ListAllAsync_IsNotLimitedToLatestFifty()
    {
        var options = new DbContextOptionsBuilder<DbScannerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new DbScannerDbContext(options, DbScanner.Application.Models.ResourceAccessContext.ForSystem());
        var target = new Target
        {
            Name = "Export Target",
            Host = "export-db.internal",
            NormalizedHost = "export-db.internal",
            Port = 5432,
            Engine = DatabaseEngine.PostgreSql
        };
        var scan = new Scan
        {
            Target = target,
            TargetId = target.Id,
            Status = ScanStatus.Scanned,
            RiskScore = 42,
            OverallSeverity = Severity.Medium
        };
        var detectedAt = DateTimeOffset.UtcNow.AddHours(-1);

        dbContext.Findings.AddRange(Enumerable.Range(1, 75).Select(index => new Finding
        {
            Scan = scan,
            ScanId = scan.Id,
            Target = target,
            TargetId = target.Id,
            RuleId = $"export-rule-{index}",
            Title = $"Export finding {index}",
            Severity = Severity.Low,
            ScoreContribution = index,
            Evidence = "Türkçe kanıt: şğüöçİı",
            Remediation = "Fix the configuration.",
            SourceScript = "export-check",
            DetectedAt = detectedAt.AddMinutes(index)
        }));
        await dbContext.SaveChangesAsync();
        var repository = new FindingRepository(dbContext);

        var latest = await repository.ListLatestAsync(50);
        var all = await repository.ListAllAsync();

        Assert.Equal(50, latest.Count);
        Assert.Equal(75, all.Count);
        Assert.Equal("export-rule-75", all[0].RuleId);
        Assert.Equal("export-rule-1", all[^1].RuleId);
    }

    [Fact]
    public async Task DiscoveryRepository_RefreshesDuplicateResultInsteadOfCreatingAnotherRecord()
    {
        var options = new DbContextOptionsBuilder<DbScannerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new DbScannerDbContext(options, DbScanner.Application.Models.ResourceAccessContext.ForSystem());
        var firstJob = DiscoveryJob("first");
        var secondJob = DiscoveryJob("second");
        dbContext.DiscoveryJobs.AddRange(firstJob, secondJob);
        dbContext.DiscoveredDatabases.Add(new DiscoveredDatabase
        {
            DiscoveryJobId = firstJob.Id,
            Host = "DB.INTERNAL.",
            NormalizedHost = "db.internal",
            EnvironmentTag = "demo",
            Port = 3306,
            DetectedService = "MySQL",
            DetectedEngine = DatabaseEngine.MySql,
            Confidence = 70,
            FingerprintLevel = DiscoveryFingerprintLevel.Probable,
            Status = DiscoveryResultStatus.Supported,
            Reason = "Initial discovery"
        });
        await dbContext.SaveChangesAsync();
        var repository = new DiscoveryRepository(dbContext);

        await repository.CompleteAsync(secondJob.Id, [new DiscoveredDatabase
        {
            DiscoveryJobId = secondJob.Id,
            Host = "db.internal",
            NormalizedHost = "db.internal",
            EnvironmentTag = "demo",
            Port = 3306,
            DetectedService = "MySQL",
            DetectedEngine = DatabaseEngine.MySql,
            Confidence = 96,
            FingerprintLevel = DiscoveryFingerprintLevel.Confirmed,
            Status = DiscoveryResultStatus.Supported,
            Reason = "Confirmed again"
        }]);

        var result = Assert.Single(await dbContext.DiscoveredDatabases.ToListAsync());
        Assert.Equal(secondJob.Id, result.DiscoveryJobId);
        Assert.Equal(DiscoveryResultStatus.AlreadyDiscovered, result.Status);
        Assert.Equal(96, result.Confidence);
    }

    [Fact]
    public async Task DiscoveryRepository_RefreshesUnsupportedResultWithoutPromotingIt()
    {
        var options = new DbContextOptionsBuilder<DbScannerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new DbScannerDbContext(options, DbScanner.Application.Models.ResourceAccessContext.ForSystem());
        var firstJob = DiscoveryJob("first-unsupported");
        var secondJob = DiscoveryJob("second-unsupported");
        dbContext.DiscoveryJobs.AddRange(firstJob, secondJob);
        dbContext.DiscoveredDatabases.Add(new DiscoveredDatabase
        {
            DiscoveryJobId = firstJob.Id,
            Host = "redis.internal",
            NormalizedHost = "redis.internal",
            EnvironmentTag = "demo",
            Port = 6379,
            DetectedService = "Redis",
            DetectedEngine = null,
            Confidence = 65,
            FingerprintLevel = DiscoveryFingerprintLevel.PortOnly,
            Status = DiscoveryResultStatus.Unsupported,
            Reason = "Redis is not supported yet."
        });
        await dbContext.SaveChangesAsync();
        var repository = new DiscoveryRepository(dbContext);

        await repository.CompleteAsync(secondJob.Id, [new DiscoveredDatabase
        {
            DiscoveryJobId = secondJob.Id,
            Host = "redis.internal",
            NormalizedHost = "redis.internal",
            EnvironmentTag = "demo",
            Port = 6379,
            DetectedService = "Redis",
            DetectedEngine = null,
            Confidence = 65,
            FingerprintLevel = DiscoveryFingerprintLevel.PortOnly,
            Status = DiscoveryResultStatus.Unsupported,
            Reason = "Redis is not supported yet."
        }]);

        var result = Assert.Single(await dbContext.DiscoveredDatabases.ToListAsync());
        Assert.Equal(secondJob.Id, result.DiscoveryJobId);
        Assert.Equal(DiscoveryResultStatus.Unsupported, result.Status);
        Assert.Equal("Redis is not supported yet.", result.Reason);
    }

    [Fact]
    public async Task DiscoveryRepository_Delete_RemovesJobAndCascadesOnlyItsResults()
    {
        var options = new DbContextOptionsBuilder<DbScannerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new DbScannerDbContext(options, DbScanner.Application.Models.ResourceAccessContext.ForSystem());
        var job = DiscoveryJob("delete-me");
        job.Status = DiscoveryStatus.Completed;
        var target = new Target
        {
            Name = "Registered MySQL",
            Host = "db.internal",
            NormalizedHost = "db.internal",
            Port = 3306,
            Engine = DatabaseEngine.MySql,
            EnvironmentTag = "demo"
        };
        var result = new DiscoveredDatabase
        {
            DiscoveryJobId = job.Id,
            Host = "db.internal",
            NormalizedHost = "db.internal",
            EnvironmentTag = "demo",
            Port = 3306,
            DetectedService = "MySQL",
            DetectedEngine = DatabaseEngine.MySql,
            Confidence = 90,
            FingerprintLevel = DiscoveryFingerprintLevel.Confirmed,
            Status = DiscoveryResultStatus.Supported,
            Reason = "Supported",
            TargetId = target.Id
        };
        job.Results.Add(result);
        dbContext.Targets.Add(target);
        dbContext.DiscoveryJobs.Add(job);
        await dbContext.SaveChangesAsync();
        var repository = new DiscoveryRepository(dbContext);

        var persistedJob = await repository.GetByIdAsync(job.Id);
        await repository.DeleteAsync(persistedJob!);
        await repository.SaveChangesAsync();

        Assert.False(await dbContext.DiscoveryJobs.AnyAsync(candidate => candidate.Id == job.Id));
        Assert.False(await dbContext.DiscoveredDatabases.AnyAsync(candidate => candidate.Id == result.Id));
        Assert.True(await dbContext.Targets.AnyAsync(candidate => candidate.Id == target.Id));
    }

    private static DiscoveryJob DiscoveryJob(string name) => new()
    {
        Name = name,
        ScopeInput = "db.internal",
        EnvironmentTag = "demo",
        SupportedEngineFilter = "mysql",
        Status = DiscoveryStatus.Running
    };
}
