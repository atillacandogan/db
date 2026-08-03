using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Application.Services;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using Xunit;

namespace DbScanner.Application.Tests;

public sealed class TargetServiceTests
{
    [Fact]
    public async Task CreateAsync_ThrowsWhenTargetIsOutsideApprovedScope()
    {
        var targetRepository = new InMemoryTargetRepository();
        var scanRepository = new StubScanRepository();
        var scopeRepository = new StubScanScopeRepository(isAllowed: false);
        var service = new TargetService(targetRepository, scanRepository, scopeRepository, new StubConnectionStringParser(), new StubCredentialProtector());

        var command = new CreateTargetCommand("Demo", "10.0.0.10", 5432, DatabaseEngine.PostgreSql, "dev", true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(command));

        Assert.Equal("Target is outside the approved scan scope.", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_ThrowsWhenDuplicateTargetExists()
    {
        var targetRepository = new InMemoryTargetRepository(new Target
        {
            Name = "Existing",
            Host = "demo-postgres-target",
            Port = 5432,
            Engine = DatabaseEngine.PostgreSql
        });
        var scanRepository = new StubScanRepository();
        var scopeRepository = new StubScanScopeRepository(isAllowed: true);
        var service = new TargetService(targetRepository, scanRepository, scopeRepository, new StubConnectionStringParser(), new StubCredentialProtector());

        var command = new CreateTargetCommand("Demo", "demo-postgres-target", 5432, DatabaseEngine.PostgreSql, "demo", true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(command));

        Assert.Equal("A target with the same host, port and engine already exists.", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_PersistsTargetWhenScopeAllows()
    {
        var targetRepository = new InMemoryTargetRepository();
        var scanRepository = new StubScanRepository();
        var scopeRepository = new StubScanScopeRepository(isAllowed: true);
        var service = new TargetService(targetRepository, scanRepository, scopeRepository, new StubConnectionStringParser(), new StubCredentialProtector());

        var result = await service.CreateAsync(
            new CreateTargetCommand("Demo", "demo-postgres-target", 5432, DatabaseEngine.PostgreSql, "demo", true));

        Assert.Equal("Demo", result.Name);
        Assert.Equal("defaultValues", result.ConnectionMode);
        Assert.Single(targetRepository.Items);
    }

    [Fact]
    public async Task CreateAsync_StoresOnlyProtectedCredentialsAndReturnsPresenceFlag()
    {
        var targetRepository = new InMemoryTargetRepository();
        var service = new TargetService(
            targetRepository,
            new StubScanRepository(),
            new StubScanScopeRepository(isAllowed: true),
            new StubConnectionStringParser(),
            new StubCredentialProtector());
        var credentials = new DatabaseAuthentication(
            "DbScannerDemo",
            "scanner",
            "must-not-be-persisted");

        var result = await service.CreateAsync(
            new CreateTargetCommand(
                "Demo",
                "demo-mssql-target",
                1433,
                DatabaseEngine.MsSql,
                "demo",
                true,
                Credentials: credentials));

        var persisted = Assert.Single(targetRepository.Items);
        Assert.Equal("protected:DbScannerDemo:scanner", persisted.ProtectedCredentials);
        Assert.DoesNotContain(credentials.Password, persisted.ProtectedCredentials);
        Assert.True(result.HasCredentials);
        Assert.DoesNotContain(
            result.GetType().GetProperties(),
            property => property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("ProtectedCredentials", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UpdateAsync_CanReplaceAndClearTargetCredentials()
    {
        var target = new Target
        {
            Name = "Demo",
            Host = "demo-mssql-target",
            Port = 1433,
            Engine = DatabaseEngine.MsSql,
            EnvironmentTag = "demo",
            ProtectedCredentials = "old-protected-value"
        };
        var repository = new InMemoryTargetRepository(target);
        var service = new TargetService(
            repository,
            new StubScanRepository(),
            new StubScanScopeRepository(isAllowed: true),
            new StubConnectionStringParser(),
            new StubCredentialProtector());

        var updated = await service.UpdateAsync(
            target.Id,
            new UpdateTargetCommand(
                target.Name,
                target.Host,
                target.Port,
                target.Engine,
                target.EnvironmentTag,
                true,
                new DatabaseAuthentication("DbScannerDemo", "scanner", "replacement-secret")));
        Assert.True(updated.HasCredentials);
        Assert.Equal("protected:DbScannerDemo:scanner", target.ProtectedCredentials);

        var cleared = await service.UpdateAsync(
            target.Id,
            new UpdateTargetCommand(
                target.Name,
                target.Host,
                target.Port,
                target.Engine,
                target.EnvironmentTag,
                true,
                ClearCredentials: true));
        Assert.False(cleared.HasCredentials);
        Assert.Null(target.ProtectedCredentials);
    }

    [Fact]
    public async Task CreateAsync_UsesParsedEndpointWithoutPersistingConnectionString()
    {
        var targetRepository = new InMemoryTargetRepository();
        var scanRepository = new StubScanRepository();
        var scopeRepository = new StubScanScopeRepository(isAllowed: true);
        var parser = new StubConnectionStringParser(
            new ParsedTargetConnection("manual-mysql", 3306, "demo", "root"));
        var service = new TargetService(targetRepository, scanRepository, scopeRepository, parser, new StubCredentialProtector());

        var result = await service.CreateAsync(
            new CreateTargetCommand(
                "Manual MySQL",
                null,
                null,
                DatabaseEngine.MySql,
                "local",
                true,
                TargetConnectionMode.ManualConnectionString,
                "Server=manual-mysql;Database=demo;User=root;Password=do-not-store;"));

        var persistedTarget = Assert.Single(targetRepository.Items);
        Assert.Equal("manual-mysql", persistedTarget.Host);
        Assert.Equal(3306, persistedTarget.Port);
        Assert.Equal(TargetConnectionMode.ManualConnectionString, persistedTarget.ConnectionMode);
        Assert.Equal("manualConnectionString", result.ConnectionMode);
        Assert.DoesNotContain(
            persistedTarget.GetType().GetProperties(),
            property => property.Name.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateAsync_PersistsSafeMsSqlEndpointFromManualConnectionString()
    {
        var targetRepository = new InMemoryTargetRepository();
        var parser = new StubConnectionStringParser(
            new ParsedTargetConnection("demo-mssql-target", 1433, "demo", "sa"));
        var service = new TargetService(
            targetRepository,
            new StubScanRepository(),
            new StubScanScopeRepository(isAllowed: true),
            parser,
            new StubCredentialProtector());

        var result = await service.CreateAsync(
            new CreateTargetCommand(
                "Manual MSSQL",
                null,
                null,
                DatabaseEngine.MsSql,
                "demo",
                true,
                TargetConnectionMode.ManualConnectionString,
                "Server=demo-mssql-target,1433;Database=demo;User Id=sa;Password=do-not-store;"));

        var persistedTarget = Assert.Single(targetRepository.Items);
        Assert.Equal("demo-mssql-target", persistedTarget.Host);
        Assert.Equal(1433, persistedTarget.Port);
        Assert.Equal(DatabaseEngine.MsSql, persistedTarget.Engine);
        Assert.Equal("mssql", result.Engine);
        Assert.DoesNotContain(
            persistedTarget.GetType().GetProperties(),
            property => property.Name.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateAsync_RequiresEnvironment()
    {
        var targetRepository = new InMemoryTargetRepository();
        var service = new TargetService(
            targetRepository,
            new StubScanRepository(),
            new StubScanScopeRepository(isAllowed: true),
            new StubConnectionStringParser(),
            new StubCredentialProtector());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(
                new CreateTargetCommand(
                    "Demo",
                    "demo-postgres-target",
                    5432,
                    DatabaseEngine.PostgreSql,
                    " ",
                    true)));

        Assert.Equal("Target environment is required.", exception.Message);
        Assert.Empty(targetRepository.Items);
    }

    private sealed class InMemoryTargetRepository(params Target[] targets) : ITargetRepository
    {
        public List<Target> Items { get; } = targets.ToList();

        public Task<IReadOnlyList<Target>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Target>>(Items);

        public Task<Target?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

        public Task AddAsync(Target target, CancellationToken cancellationToken = default)
        {
            Items.Add(target);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Target target, CancellationToken cancellationToken = default)
        {
            Items.Remove(target);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubScanScopeRepository(bool isAllowed) : IScanScopeRepository
    {
        public Task<IReadOnlyList<ScanScopeAllowlist>> ListEnabledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScanScopeAllowlist>>(Array.Empty<ScanScopeAllowlist>());

        public Task<bool> IsAllowedAsync(string host, int port, CancellationToken cancellationToken = default) =>
            Task.FromResult(isAllowed);
    }

    private sealed class StubConnectionStringParser(
        ParsedTargetConnection? parsedConnection = null) : ITargetConnectionStringParser
    {
        public ParsedTargetConnection Parse(string connectionString, DatabaseEngine engine) =>
            parsedConnection
            ?? throw new InvalidOperationException("Connection string parser was not expected to run.");
    }

    private sealed class StubCredentialProtector : IScanCredentialProtector
    {
        public string Protect(DatabaseAuthentication authentication) =>
            $"protected:{authentication.Database}:{authentication.Username}";

        public DatabaseAuthentication Unprotect(string protectedCredentials) =>
            throw new InvalidOperationException("Credential unprotect was not expected.");
    }

    private sealed class StubScanRepository : IScanRepository
    {
        public Task<Scan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Scan?>(null);
        public Task<IReadOnlyList<Scan>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Scan>>(Array.Empty<Scan>());
        public Task<IReadOnlyList<Scan>> ListByTargetAsync(Guid targetId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Scan>>(Array.Empty<Scan>());
        public Task<bool> HasActiveScanAsync(Guid targetId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task AddAsync(Scan scan, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Scan scan, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> TryMarkScanningAsync(Guid scanId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task IncrementRetryCountAsync(Guid scanId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StoreExecutionArtifactsAsync(Guid scanId, IReadOnlyCollection<ScanScriptResult> scriptResults, IReadOnlyCollection<Finding> findings, int? riskScore, Severity? overallSeverity, ScanStatus status, string? errorMessage = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateStatusAsync(Guid scanId, ScanStatus status, string? errorMessage = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void ClearTracking() { }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
