using DbScanner.Application.Models;
using DbScanner.Domain.Entities;
using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

public sealed class ScanCompatibilityReadinessServiceTests
{
    [Fact]
    public async Task GetAsync_ReportsSeededDatabaseReadyForLegacyRemoval()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();
        var service = new ScanCompatibilityReadinessService(
            dbContext,
            NullLogger<ScanCompatibilityReadinessService>.Instance);

        var readiness = await service.GetAsync();

        Assert.True(readiness.ReadyForLegacyRemoval);
        Assert.True(readiness.CatalogRows > 0);
        Assert.True(readiness.RuleMappings > 0);
    }

    [Fact]
    public async Task GetAsync_BlocksRemovalWhenLegacyFallbackRowsRemain()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();
        var script = await dbContext.NseScripts.SingleAsync(item => item.Id == "banner");
        script.ExecutorType = null;
        var mapping = await dbContext.RuleNseScripts.FirstAsync();
        mapping.ScanCheckId = null;
        dbContext.ScanScriptResults.Add(new ScanScriptResult
        {
            ScanId = Guid.NewGuid(),
            NseScriptId = script.Id,
            ScanCheckId = null,
            ScriptName = script.NseScriptName
        });
        await dbContext.SaveChangesAsync();
        var service = new ScanCompatibilityReadinessService(
            dbContext,
            NullLogger<ScanCompatibilityReadinessService>.Instance);

        var readiness = await service.GetAsync();

        Assert.False(readiness.ReadyForLegacyRemoval);
        Assert.Equal(1, readiness.CatalogRowsMissingExecutorType);
        Assert.Equal(1, readiness.RuleMappingsMissingScanCheckId);
        Assert.Equal(1, readiness.ScanResultsMissingScanCheckId);
    }

    private static DbScannerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<DbScannerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new DbScannerDbContext(options, DbScanner.Application.Models.ResourceAccessContext.ForSystem());
    }
}
