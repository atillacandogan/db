using System.Text.Json;
using DbScanner.Api.Contracts;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Application.Services;
using DbScanner.Contracts.Scans;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using Xunit;

namespace DbScanner.Application.Tests;

public sealed class ScanCheckCatalogServiceTests
{
    [Fact]
    public async Task ListAsync_DualReadsLegacyAndExplicitExecutorMetadata()
    {
        var legacy = CreateScript("oracle-auth-version", executorType: null);
        var generic = CreateScript("vendor-check", "vendor-custom-scanner");
        var service = new ScanCheckCatalogService(
            new InMemoryRepository(legacy, generic),
            CreateResolver(
                CreateCapability(ScanExecutorTypes.OracleNative, DatabaseEngine.Oracle),
                CreateCapability("vendor-custom-scanner", DatabaseEngine.Generic)));

        var checks = await service.ListAsync();

        Assert.Equal(ScanExecutorTypes.OracleNative, checks[0].ExecutorType);
        Assert.Equal("vendor-custom-scanner", checks[1].ExecutorType);
    }

    [Fact]
    public async Task CreateAsync_PersistsExecutorNeutralMetadataInLegacyTable()
    {
        var repository = new InMemoryRepository();
        var service = new ScanCheckCatalogService(
            repository,
            CreateResolver(CreateCapability(
                ScanExecutorTypes.PythonAgent,
                DatabaseEngine.PostgreSql,
                allowsManualCreation: true,
                supportsConfigurationSchema: true)));

        var result = await service.CreateAsync(new CreateScanCheckCommand(
            "python-posture",
            "postgres-posture",
            "PostgreSQL posture",
            "Collects posture through a future agent.",
            ScanExecutorTypes.PythonAgent,
            ["postgresql"],
            "configuration",
            true,
            true,
            false,
            "{\"type\":\"object\"}",
            "generic_output",
            "key_value",
            "secure=true",
            "medium",
            5,
            "Apply the secure posture.",
            "https://example.test/posture",
            "approved"));

        var stored = Assert.Single(repository.Scripts);
        Assert.Equal(ScanExecutorTypes.PythonAgent, stored.ExecutorType);
        Assert.Equal("postgres-posture", stored.NseScriptName);
        Assert.True(stored.RequiresCredentials);
        Assert.Equal("{\"type\":\"object\"}", stored.AllowedArgsSchema);
        Assert.Equal(ScanExecutorTypes.PythonAgent, result.ExecutorType);
        Assert.Equal("postgres-posture", result.Name);
        Assert.Equal(1, repository.SaveChangesCount);
    }

    [Fact]
    public async Task CreateAsync_RejectsUnsafeExecutorIdentifier()
    {
        var service = new ScanCheckCatalogService(
            new InMemoryRepository(),
            CreateResolver());
        var command = new CreateScanCheckCommand(
            "unsafe",
            "unsafe",
            "Unsafe",
            "Unsafe executor fixture.",
            "../../bin/scanner",
            ["generic"],
            null,
            false,
            false,
            false,
            null,
            "generic_output",
            "contains",
            "unsafe",
            "info",
            0,
            null,
            null,
            "draft");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(command));

        Assert.Equal("Executor type is invalid.", error.Message);
    }

    [Fact]
    public async Task CreateAsync_RejectsExecutorThatDoesNotAllowManualDefinitions()
    {
        var service = new ScanCheckCatalogService(
            new InMemoryRepository(),
            CreateResolver(CreateCapability(ScanExecutorTypes.MsSqlNative, DatabaseEngine.MsSql)));
        var command = new CreateScanCheckCommand(
            "custom-mssql",
            "custom-mssql",
            "Custom MSSQL",
            "Unsupported manual native definition.",
            ScanExecutorTypes.MsSqlNative,
            ["mssql"],
            null,
            false,
            false,
            false,
            null,
            "generic_output",
            "contains",
            "ok",
            "info",
            0,
            null,
            null,
            "draft");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(command));

        Assert.Contains("does not allow manual", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScanMessage_DeserializesOldAndNewSelectionFields()
    {
        const string oldJson = """
            {
              "scanId":"00000000-0000-0000-0000-000000000001",
              "targetId":"00000000-0000-0000-0000-000000000002",
              "engine":"postgresql",
              "host":"db.internal",
              "port":5432,
              "requestedAt":"2026-10-03T00:00:00+00:00",
              "selectedNseScriptIds":["banner"]
            }
            """;
        const string newJson = """
            {
              "scanId":"00000000-0000-0000-0000-000000000001",
              "targetId":"00000000-0000-0000-0000-000000000002",
              "engine":"postgresql",
              "host":"db.internal",
              "port":5432,
              "requestedAt":"2026-10-03T00:00:00+00:00",
              "selectedCheckIds":["banner"]
            }
            """;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var oldMessage = JsonSerializer.Deserialize<ScanRequestedMessage>(oldJson, options)!;
        var newMessage = JsonSerializer.Deserialize<ScanRequestedMessage>(newJson, options)!;

        Assert.Equal(["banner"], oldMessage.SelectedNseScriptIds);
        Assert.Null(oldMessage.SelectedCheckIds);
        Assert.Equal(["banner"], newMessage.SelectedCheckIds);
        Assert.Null(newMessage.SelectedNseScriptIds);
    }

    [Fact]
    public void CreateScanRequest_AcceptsBothContractGenerations()
    {
        var legacy = new CreateScanRequest(Guid.NewGuid(), ["banner"]);
        var generic = new CreateScanRequest(Guid.NewGuid(), SelectedCheckIds: ["banner"]);

        Assert.Equal(["banner"], legacy.SelectedNseScriptIds);
        Assert.Equal(["banner"], generic.SelectedCheckIds);
    }

    [Fact]
    public void SelectionCompatibility_RejectsConflictingOldAndNewFields()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ScanCheckSelectionCompatibility.Resolve(
                ["new-check"],
                ["legacy-check"]));

        Assert.Contains("same checks", error.Message, StringComparison.Ordinal);
        var resolved = ScanCheckSelectionCompatibility.Resolve(
            [" BANNER "],
            ["banner"]);
        Assert.Equal("banner", Assert.Single(resolved!), ignoreCase: true);
    }

    private static NseScript CreateScript(string id, string? executorType) =>
        new()
        {
            Id = id,
            ExecutorType = executorType,
            NseScriptName = id,
            DisplayName = id,
            Description = id,
            SupportedEngines = "generic",
            MatcherExpression = id,
            ApprovalStatus = ScriptApprovalStatus.Approved,
            Source = NseScriptSource.Manual
        };

    private static ScanExecutorCapability CreateCapability(
        string executorType,
        DatabaseEngine engine,
        bool allowsManualCreation = false,
        bool supportsConfigurationSchema = false) =>
        new(
            executorType,
            executorType,
            "Test executor.",
            [engine],
            allowsManualCreation,
            AllowsCheckNameEditing: false,
            supportsConfigurationSchema);

    private static IScanExecutorStrategyResolver CreateResolver(
        params ScanExecutorCapability[] capabilities) =>
        new ScanExecutorStrategyResolver(
            capabilities.Select(capability => new StaticStrategy(capability)));

    private sealed class StaticStrategy(ScanExecutorCapability capability) : IScanExecutorStrategy
    {
        public string ExecutorType => capability.ExecutorType;
        public ScanExecutorCapability Capability => capability;

        public Task<ScanExecutorResult> ExecuteAsync(
            ScanExecutionContext context,
            IReadOnlyCollection<ScanCheckDefinition> checks,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class InMemoryRepository(params NseScript[] scripts) : INseScriptRepository
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
            Task.FromResult(Scripts.SingleOrDefault(script =>
                string.Equals(script.Id, id, StringComparison.OrdinalIgnoreCase)));

        public Task AddAsync(NseScript script, CancellationToken cancellationToken = default)
        {
            Scripts.Add(script);
            return Task.CompletedTask;
        }

        public void Delete(NseScript script) => Scripts.Remove(script);

        public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;
            return Task.CompletedTask;
        }
    }
}
