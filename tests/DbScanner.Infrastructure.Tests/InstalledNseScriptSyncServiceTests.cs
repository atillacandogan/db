using DbScanner.Infrastructure.Persistence;
using DbScanner.Infrastructure.Repositories;
using DbScanner.Infrastructure.Scanning;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

public sealed class InstalledNseScriptSyncServiceTests
{
    [Fact]
    public async Task SyncInstalledScriptsAsync_IsIdempotentAndSkipsInvalidNumericOnlyScriptName()
    {
        var scriptDirectory = Path.Combine(Path.GetTempPath(), $"dbscanner-nse-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scriptDirectory);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(scriptDirectory, "2.nse"), "description = \"2\"");
            await File.WriteAllTextAsync(
                Path.Combine(scriptDirectory, "banner.nse"),
                "description = \"Collects a banner.\" categories = { \"safe\" }");

            var dbOptions = new DbContextOptionsBuilder<DbScannerDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
            await using var dbContext = new DbScannerDbContext(dbOptions, DbScanner.Application.Models.ResourceAccessContext.ForSystem());
            var repository = new NseScriptRepository(dbContext);
            var service = new InstalledNseScriptSyncService(
                repository,
                Options.Create(new NseScriptDiscoveryOptions { ScriptDirectory = scriptDirectory }),
                NullLogger<InstalledNseScriptSyncService>.Instance);

            var result = await service.SyncInstalledScriptsAsync();
            var secondResult = await service.SyncInstalledScriptsAsync();
            var scripts = await repository.ListAsync();

            Assert.Equal(2, result.DiscoveredCount);
            Assert.Equal(1, result.AddedCount);
            Assert.Equal(0, result.UpdatedCount);
            Assert.Equal(1, result.SkippedCount);
            Assert.Equal(0, result.FailedCount);
            Assert.Equal(0, secondResult.AddedCount);
            Assert.Equal(0, secondResult.UpdatedCount);
            Assert.Equal(1, secondResult.UnchangedCount);
            Assert.Equal(1, secondResult.SkippedCount);
            Assert.Equal(0, secondResult.FailedCount);
            Assert.Single(scripts);
            Assert.DoesNotContain(scripts, script => script.Id == "2");
            Assert.Contains(scripts, script => script.Id == "banner");
        }
        finally
        {
            Directory.Delete(scriptDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SyncInstalledScriptsAsync_UpdatesMetadataMarksMissingAndPreservesManualScripts()
    {
        var scriptDirectory = Path.Combine(Path.GetTempPath(), $"dbscanner-nse-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scriptDirectory);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(scriptDirectory, "banner.nse"),
                "description = \"Fresh banner description.\" categories = { \"safe\", \"discovery\" }");
            await File.WriteAllTextAsync(
                Path.Combine(scriptDirectory, "manual-check.nse"),
                "description = \"Container metadata must not overwrite manual data.\"");

            var dbOptions = new DbContextOptionsBuilder<DbScannerDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
            await using (var arrangeContext = new DbScannerDbContext(dbOptions, DbScanner.Application.Models.ResourceAccessContext.ForSystem()))
            {
                arrangeContext.NseScripts.AddRange(
                    CreateScript("banner", NseScriptSource.Installed, "Stale description", true),
                    CreateScript("removed-script", NseScriptSource.Installed, "Removed", true),
                    CreateScript("manual-check", NseScriptSource.Manual, "Manual description", null));
                await arrangeContext.SaveChangesAsync();
            }

            await using var syncContext = new DbScannerDbContext(dbOptions, DbScanner.Application.Models.ResourceAccessContext.ForSystem());
            var repository = new NseScriptRepository(syncContext);
            var service = new InstalledNseScriptSyncService(
                repository,
                Options.Create(new NseScriptDiscoveryOptions { ScriptDirectory = scriptDirectory }),
                NullLogger<InstalledNseScriptSyncService>.Instance);

            var result = await service.SyncInstalledScriptsAsync();
            var scripts = await repository.ListAsync();

            var banner = Assert.Single(scripts, script => script.Id == "banner");
            Assert.Equal("Fresh banner description.", banner.Description);
            Assert.Equal("discovery,safe", banner.Categories);
            Assert.True(banner.IsInstalled);

            var removed = Assert.Single(scripts, script => script.Id == "removed-script");
            Assert.False(removed.IsInstalled);

            var manual = Assert.Single(scripts, script => script.Id == "manual-check");
            Assert.Equal(NseScriptSource.Manual, manual.Source);
            Assert.Equal("Manual description", manual.Description);
            Assert.Null(manual.IsInstalled);

            Assert.Equal(0, result.AddedCount);
            Assert.Equal(2, result.UpdatedCount);
            Assert.Equal(0, result.UnchangedCount);
            Assert.Equal(1, result.SkippedCount);
            Assert.Equal(0, result.FailedCount);
            Assert.Equal(1, result.UninstalledCount);
            Assert.Equal(3, scripts.Count);
        }
        finally
        {
            Directory.Delete(scriptDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SyncInstalledScriptsAsync_PreservesInstalledRecordsWhenSourceDirectoryIsEmpty()
    {
        var scriptDirectory = Path.Combine(Path.GetTempPath(), $"dbscanner-nse-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scriptDirectory);

        try
        {
            var dbOptions = new DbContextOptionsBuilder<DbScannerDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
            await using var dbContext = new DbScannerDbContext(dbOptions, DbScanner.Application.Models.ResourceAccessContext.ForSystem());
            dbContext.NseScripts.Add(CreateScript("banner", NseScriptSource.Installed, "Banner", true));
            await dbContext.SaveChangesAsync();

            var repository = new NseScriptRepository(dbContext);
            var service = new InstalledNseScriptSyncService(
                repository,
                Options.Create(new NseScriptDiscoveryOptions { ScriptDirectory = scriptDirectory }),
                NullLogger<InstalledNseScriptSyncService>.Instance);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SyncInstalledScriptsAsync());

            Assert.Contains("existing installed records were preserved", exception.Message);
            Assert.True(Assert.Single(await repository.ListAsync()).IsInstalled);
        }
        finally
        {
            Directory.Delete(scriptDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SyncInstalledScriptsAsync_ContinuesAfterUnreadableScriptAndPreservesItsExistingRecord()
    {
        var scriptDirectory = Path.Combine(Path.GetTempPath(), $"dbscanner-nse-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scriptDirectory);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(scriptDirectory, "banner.nse"),
                "description = \"Collects a banner.\" categories = { \"safe\" }");
            File.CreateSymbolicLink(
                Path.Combine(scriptDirectory, "broken.nse"),
                Path.Combine(scriptDirectory, "missing-target.nse"));

            var dbOptions = new DbContextOptionsBuilder<DbScannerDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
            await using var dbContext = new DbScannerDbContext(dbOptions, DbScanner.Application.Models.ResourceAccessContext.ForSystem());
            dbContext.NseScripts.Add(CreateScript("broken", NseScriptSource.Installed, "Existing", true));
            await dbContext.SaveChangesAsync();

            var repository = new NseScriptRepository(dbContext);
            var service = new InstalledNseScriptSyncService(
                repository,
                Options.Create(new NseScriptDiscoveryOptions { ScriptDirectory = scriptDirectory }),
                NullLogger<InstalledNseScriptSyncService>.Instance);

            var result = await service.SyncInstalledScriptsAsync();
            var scripts = await repository.ListAsync();

            Assert.Equal(2, result.DiscoveredCount);
            Assert.Equal(1, result.AddedCount);
            Assert.Equal(1, result.FailedCount);
            Assert.Equal(0, result.UninstalledCount);
            Assert.True(Assert.Single(scripts, script => script.Id == "broken").IsInstalled);
            Assert.Contains(scripts, script => script.Id == "banner");
        }
        finally
        {
            Directory.Delete(scriptDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SyncInstalledScriptsAsync_InfersMsSqlEngineWithoutEnablingRiskyScripts()
    {
        var scriptDirectory = Path.Combine(Path.GetTempPath(), $"dbscanner-nse-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scriptDirectory);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(scriptDirectory, "ms-sql-info.nse"),
                "description = \"MSSQL information.\" categories = { \"safe\", \"discovery\" }");
            await File.WriteAllTextAsync(
                Path.Combine(scriptDirectory, "ms-sql-empty-password.nse"),
                "description = \"MSSQL password check.\" categories = { \"auth\", \"intrusive\" }");

            var dbOptions = new DbContextOptionsBuilder<DbScannerDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
            await using var dbContext = new DbScannerDbContext(dbOptions, DbScanner.Application.Models.ResourceAccessContext.ForSystem());
            var repository = new NseScriptRepository(dbContext);
            var service = new InstalledNseScriptSyncService(
                repository,
                Options.Create(new NseScriptDiscoveryOptions { ScriptDirectory = scriptDirectory }),
                NullLogger<InstalledNseScriptSyncService>.Instance);

            await service.SyncInstalledScriptsAsync();
            var scripts = await repository.ListAsync();

            Assert.Equal(2, scripts.Count);
            Assert.All(scripts, script => Assert.Equal("mssql", script.SupportedEngines));
            Assert.All(scripts, script => Assert.False(script.DefaultEnabled));
            Assert.All(scripts, script => Assert.Equal(ScriptApprovalStatus.Draft, script.ApprovalStatus));

            var riskyScript = Assert.Single(scripts, script => script.Id == "ms-sql-empty-password");
            Assert.True(riskyScript.RequiresCredentials);
            Assert.True(riskyScript.IsIntrusive);
        }
        finally
        {
            Directory.Delete(scriptDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SyncInstalledScriptsAsync_InfersOracleEngineFromScriptName()
    {
        var scriptDirectory = Path.Combine(Path.GetTempPath(), $"dbscanner-nse-{Guid.NewGuid():N}");
        Directory.CreateDirectory(scriptDirectory);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(scriptDirectory, "oracle-tns-version.nse"),
                "description = \"Oracle TNS version.\" categories = { \"safe\", \"version\" }");

            var dbOptions = new DbContextOptionsBuilder<DbScannerDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options;
            await using var dbContext = new DbScannerDbContext(dbOptions, DbScanner.Application.Models.ResourceAccessContext.ForSystem());
            var repository = new NseScriptRepository(dbContext);
            var service = new InstalledNseScriptSyncService(
                repository,
                Options.Create(new NseScriptDiscoveryOptions { ScriptDirectory = scriptDirectory }),
                NullLogger<InstalledNseScriptSyncService>.Instance);

            await service.SyncInstalledScriptsAsync();
            var script = Assert.Single(await repository.ListAsync());

            Assert.Equal("oracle", script.SupportedEngines);
            Assert.Equal(DatabaseEngine.Oracle, Assert.Single(script.Matchers).Engine);
        }
        finally
        {
            Directory.Delete(scriptDirectory, recursive: true);
        }
    }

    private static NseScript CreateScript(
        string id,
        NseScriptSource source,
        string description,
        bool? isInstalled) =>
        new()
        {
            Id = id,
            NseScriptName = id,
            DisplayName = id,
            Description = description,
            SupportedEngines = "generic",
            MatcherExpression = id,
            Source = source,
            IsInstalled = isInstalled
        };
}
