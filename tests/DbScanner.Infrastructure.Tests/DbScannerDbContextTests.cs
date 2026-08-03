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

        await using var dbContext = new DbScannerDbContext(options);
        await dbContext.Database.EnsureCreatedAsync();

        Assert.True(await dbContext.Targets.AnyAsync());
        Assert.True(await dbContext.Rules.AnyAsync());
        Assert.True(await dbContext.NseScripts.AnyAsync());
        Assert.True(await dbContext.ScanScopeAllowlist.AnyAsync());
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

        await using var dbContext = new DbScannerDbContext(options);
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

        await using var dbContext = new DbScannerDbContext(options);
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
}
