using DbScanner.Application.Ports;
using DbScanner.Application.Services;
using DbScanner.Application.Models;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using Xunit;

namespace DbScanner.Application.Tests;

public sealed class NseScriptCatalogServiceTests
{
    [Fact]
    public async Task DeleteAsync_RejectsInstalledScript()
    {
        var script = CreateScript("banner", NseScriptSource.Installed);
        var repository = new InMemoryNseScriptRepository(script);
        var service = new NseScriptCatalogService(repository);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(script.Id));

        Assert.Equal("Installed NSE scripts cannot be deleted. Disable the script instead.", exception.Message);
        Assert.Contains(script, repository.Scripts);
    }

    [Fact]
    public async Task DeleteAsync_RemovesManualScript()
    {
        var script = CreateScript("custom-company-check", NseScriptSource.Manual);
        var repository = new InMemoryNseScriptRepository(script);
        var service = new NseScriptCatalogService(repository);

        var deleted = await service.DeleteAsync(script.Id);

        Assert.True(deleted);
        Assert.Empty(repository.Scripts);
        Assert.Equal(1, repository.SaveChangesCount);
    }

    [Fact]
    public async Task UpdateAsync_UsesCanonicalTypeNamesAndDisablesDefaultForDisabledScript()
    {
        var script = CreateScript("custom-company-check", NseScriptSource.Manual);
        var repository = new InMemoryNseScriptRepository(script);
        var service = new NseScriptCatalogService(repository);

        var result = await service.UpdateAsync(
            script.Id,
            new UpdateNseScriptCommand(
                "Custom Company Check",
                "Custom check description.",
                ["generic"],
                true,
                "disabled",
                "key_value",
                "generic_output",
                "status",
                "info",
                0,
                "safe",
                null,
                null));

        Assert.Equal(ParserType.GenericOutput, script.ParserType);
        Assert.Equal(MatcherType.KeyValue, script.MatcherType);
        Assert.False(script.DefaultEnabled);
        Assert.Equal("generic_output", result.ParserType);
        Assert.Equal("key_value", result.MatcherType);
        Assert.False(result.DefaultEnabled);
        Assert.Equal("disabled", result.ApprovalStatus);
        Assert.Equal(1, repository.SaveChangesCount);
    }

    [Fact]
    public async Task UpdateAsync_AcceptsLegacyCompactTypeNames()
    {
        var script = CreateScript("legacy-script", NseScriptSource.Manual);
        var repository = new InMemoryNseScriptRepository(script);
        var service = new NseScriptCatalogService(repository);

        var result = await service.UpdateAsync(
            script.Id,
            new UpdateNseScriptCommand(
                "Legacy Script",
                "Legacy response round-trip.",
                ["generic"],
                false,
                "draft",
                "keyvalue",
                "genericoutput",
                "status",
                "info",
                0,
                null,
                null,
                null));

        Assert.Equal("generic_output", result.ParserType);
        Assert.Equal("key_value", result.MatcherType);
    }

    private static NseScript CreateScript(string id, NseScriptSource source) =>
        new()
        {
            Id = id,
            NseScriptName = id,
            DisplayName = id,
            Description = id,
            SupportedEngines = "generic",
            MatcherExpression = id,
            Source = source
        };

    private sealed class InMemoryNseScriptRepository(params NseScript[] scripts) : INseScriptRepository
    {
        public List<NseScript> Scripts { get; } = scripts.ToList();
        public int SaveChangesCount { get; private set; }

        public Task<IReadOnlyList<NseScript>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NseScript>>(Scripts);

        public Task<IReadOnlyList<NseScript>> ListInstalledForSyncAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NseScript>>(Scripts.Where(script => script.Source == NseScriptSource.Installed).ToArray());

        public Task<IReadOnlyList<NseScript>> ListApprovedByEngineAsync(DatabaseEngine engine, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NseScript>>(Scripts);

        public Task<IReadOnlyList<NseScript>> ListRunnableByIdsAsync(DatabaseEngine engine, IReadOnlyCollection<string> scriptIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NseScript>>(Scripts.Where(script => scriptIds.Contains(script.Id)).ToArray());

        public Task<NseScript?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scripts.SingleOrDefault(script => script.Id == id));

        public Task AddAsync(NseScript script, CancellationToken cancellationToken = default)
        {
            Scripts.Add(script);
            return Task.CompletedTask;
        }

        public void Delete(NseScript script) =>
            Scripts.Remove(script);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;
            return Task.CompletedTask;
        }
    }
}
