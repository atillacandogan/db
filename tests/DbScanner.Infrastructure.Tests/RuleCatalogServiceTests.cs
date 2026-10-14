using DbScanner.Application.Models;
using DbScanner.Application.Services;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Persistence;
using DbScanner.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

public sealed class RuleCatalogServiceTests
{
    [Fact]
    public async Task NewEngineSupport_IsTrackedAsAdded()
    {
        var options = new DbContextOptionsBuilder<DbScannerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new DbScannerDbContext(options, DbScanner.Application.Models.ResourceAccessContext.ForSystem());
        await dbContext.Database.EnsureCreatedAsync();

        var rule = await dbContext.Rules
            .Include(candidate => candidate.EngineSupport)
            .SingleAsync(candidate => candidate.Id == "mysql_service_detected");
        var newSupport = new RuleEngineSupport
        {
            RuleId = rule.Id,
            Engine = DatabaseEngine.PostgreSql,
            UnsupportedStatus = RuleResultStatus.NotSupported
        };

        rule.EngineSupport.Add(newSupport);
        dbContext.ChangeTracker.DetectChanges();

        Assert.Equal(EntityState.Added, dbContext.Entry(newSupport).State);
    }

    [Fact]
    public async Task UpdateRuleAsync_PreservesUnchangedRelationships()
    {
        var options = new DbContextOptionsBuilder<DbScannerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new DbScannerDbContext(options, DbScanner.Application.Models.ResourceAccessContext.ForSystem());
        await dbContext.Database.EnsureCreatedAsync();

        var repository = new RuleRepository(dbContext);
        var originalRule = await repository.GetByIdAsync("generic_db_port_open")
            ?? throw new InvalidOperationException("Seeded rule was not found.");
        var originalSupportIds = originalRule.EngineSupport
            .Select(support => support.Id)
            .OrderBy(id => id)
            .ToArray();
        var originalScriptMappings = originalRule.RuleScripts.ToArray();

        dbContext.ChangeTracker.Clear();

        var service = new RuleCatalogService(repository);
        var updatedRule = await service.UpdateRuleAsync(
            "generic_db_port_open",
            new UpdateRuleCommand(
                "DB port open updated",
                "Database service port is reachable.",
                "generic",
                "info",
                0,
                true,
                "structured_xml",
                "contains",
                "open",
                "Confirm the database service is expected to be reachable from the scanner network.",
                ["postgresql", "mysql"],
                ["banner"]));

        Assert.Equal("DB port open updated", updatedRule.Title);
        Assert.Equal(["mysql", "postgresql"], updatedRule.SupportedEngines);
        Assert.Equal(["banner"], updatedRule.ScanChecks);
        Assert.Equal(["banner"], updatedRule.ScanChecks);

        dbContext.ChangeTracker.Clear();
        var persistedRule = await repository.GetByIdAsync("generic_db_port_open")
            ?? throw new InvalidOperationException("Updated rule was not found.");

        Assert.Equal(
            originalSupportIds,
            persistedRule.EngineSupport.Select(support => support.Id).OrderBy(id => id));
        Assert.Equal(originalScriptMappings.Length, persistedRule.RuleScripts.Count);
        Assert.Equal(
            originalScriptMappings.Select(mapping => mapping.NseScriptId),
            persistedRule.RuleScripts.Select(mapping => mapping.NseScriptId));
        Assert.All(
            persistedRule.RuleScripts,
            mapping => Assert.Equal(mapping.NseScriptId, mapping.ScanCheckId));
    }

    [Fact]
    public async Task UpdateRuleAsync_AppliesRelationshipDifferences()
    {
        var options = new DbContextOptionsBuilder<DbScannerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new DbScannerDbContext(options, DbScanner.Application.Models.ResourceAccessContext.ForSystem());
        await dbContext.Database.EnsureCreatedAsync();

        var repository = new RuleRepository(dbContext);
        var service = new RuleCatalogService(repository);

        var updatedRule = await service.UpdateRuleAsync(
            "generic_db_port_open",
            new UpdateRuleCommand(
                "DB port open",
                "Database service port is reachable.",
                "generic",
                "info",
                0,
                true,
                "structured_xml",
                "contains",
                "open",
                "Confirm the database service is expected to be reachable from the scanner network.",
                ["postgresql"],
                ["mysql-info"]));

        Assert.Equal(["postgresql"], updatedRule.SupportedEngines);
        Assert.Equal(["mysql-info"], updatedRule.ScanChecks);
        Assert.Equal(["mysql-info"], updatedRule.ScanChecks);
    }

    [Fact]
    public async Task CreateRuleAsync_AcceptsMsSqlEngineAndSupport()
    {
        var options = new DbContextOptionsBuilder<DbScannerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        await using var dbContext = new DbScannerDbContext(options, DbScanner.Application.Models.ResourceAccessContext.ForSystem());
        await dbContext.Database.EnsureCreatedAsync();

        var service = new RuleCatalogService(new RuleRepository(dbContext));
        var rule = await service.CreateRuleAsync(
            new CreateRuleCommand(
                "mssql_test_rule",
                "MSSQL test rule",
                "MSSQL test description.",
                "mssql",
                "info",
                0,
                true,
                "structured_xml",
                "contains",
                "ms-sql-s",
                "Confirm MSSQL exposure.",
                ["mssql"],
                ["banner"]));

        Assert.Equal("mssql", rule.Engine);
        Assert.Equal(["mssql"], rule.SupportedEngines);
        Assert.Equal(["banner"], rule.ScanChecks);
        Assert.Equal(["banner"], rule.ScanChecks);
    }

    [Fact]
    public async Task UpdateRuleAsync_AcceptsGenericOnlyCheckMappingAndDualWritesLegacyColumn()
    {
        var options = new DbContextOptionsBuilder<DbScannerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        await using var dbContext = new DbScannerDbContext(options, DbScanner.Application.Models.ResourceAccessContext.ForSystem());
        await dbContext.Database.EnsureCreatedAsync();
        var repository = new RuleRepository(dbContext);
        var service = new RuleCatalogService(repository);

        var updated = await service.UpdateRuleAsync(
            "generic_db_port_open",
            new UpdateRuleCommand(
                "DB port open",
                "Database service port is reachable.",
                "generic",
                "info",
                0,
                true,
                "structured_xml",
                "contains",
                "open",
                "Confirm expected exposure.",
                ["postgresql"],
                NseScripts: null,
                ScanChecks: ["mysql-info"]));

        Assert.Equal(["mysql-info"], updated.ScanChecks);
        Assert.Equal(["mysql-info"], updated.ScanChecks);

        dbContext.ChangeTracker.Clear();
        var persisted = await repository.GetByIdAsync("generic_db_port_open")
            ?? throw new InvalidOperationException("Updated rule was not found.");
        var mapping = Assert.Single(persisted.RuleScripts);
        Assert.Equal("mysql-info", mapping.NseScriptId);
        Assert.Equal("mysql-info", mapping.ScanCheckId);
    }
}
