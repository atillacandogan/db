using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Application.Services;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using Xunit;

namespace DbScanner.Application.Tests;

public sealed class DiscoveryAllowlistServiceTests
{
    [Fact]
    public async Task CreateAsync_NormalizesTargetAndAddsStandardAndCustomPorts()
    {
        var repository = new AllowlistRepository();
        var service = new DiscoveryAllowlistService(repository, new DiscoveryConfigurationStub());

        var result = await service.CreateAsync(Command(
            targetValue: "DB.INTERNAL.",
            ports: new Dictionary<string, IReadOnlyList<int>> { ["mysql"] = [13306] }));

        Assert.Equal("db.internal", result.TargetValue);
        Assert.Equal([3306, 13306], result.AllowedPorts["mysql"]);
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateTargetInSameEnvironment()
    {
        var repository = new AllowlistRepository(new ScanScopeAllowlist
        {
            Name = "existing",
            EnvironmentTag = "demo",
            ScopeType = ScanScopeType.Hostname,
            ScopeValue = "db.internal"
        });
        var service = new DiscoveryAllowlistService(repository, new DiscoveryConfigurationStub());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(Command()));

        Assert.Contains("already exists", error.Message);
    }

    [Fact]
    public async Task CreateAsync_RejectsDuplicateAndOutOfRangePorts()
    {
        var service = new DiscoveryAllowlistService(new AllowlistRepository(), new DiscoveryConfigurationStub());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(Command(
            ports: new Dictionary<string, IReadOnlyList<int>> { ["mysql"] = [13306, 13306] })));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(Command(
            ports: new Dictionary<string, IReadOnlyList<int>> { ["mysql"] = [70000] })));
    }

    [Fact]
    public async Task CreateAsync_RejectsIpv6WithExplicitMessage()
    {
        var service = new DiscoveryAllowlistService(new AllowlistRepository(), new DiscoveryConfigurationStub());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            Command(targetValue: "2001:db8::1", targetType: "IPv4")));

        Assert.Equal("IPv6 discovery is not currently supported.", error.Message);
    }

    [Fact]
    public async Task CreateOrMergeAsync_MergesDiscoveredEngineAndPortIntoExistingIpEntry()
    {
        var existing = new ScanScopeAllowlist
        {
            Name = "Discovered host",
            EnvironmentTag = "prod",
            ScopeType = ScanScopeType.IpAddress,
            ScopeValue = "10.20.30.40",
            AllowedEngines = "mysql",
            AllowedPorts = "mysql:3306",
            Enabled = true
        };
        var service = new DiscoveryAllowlistService(
            new AllowlistRepository(existing),
            new DiscoveryConfigurationStub());

        var merged = await service.CreateOrMergeAsync(new UpsertDiscoveryAllowlistCommand(
            "Discovered host",
            "prod",
            "IPv4",
            "10.20.30.40",
            ["postgresql"],
            new Dictionary<string, IReadOnlyList<int>> { ["postgresql"] = [15432] },
            true,
            null));

        Assert.Equal(["mysql", "postgresql"], merged.AllowedEngines);
        Assert.Equal([5432, 15432], merged.AllowedPorts["postgresql"]);
        Assert.Equal([3306], merged.AllowedPorts["mysql"]);
    }

    private static UpsertDiscoveryAllowlistCommand Command(
        string targetValue = "db.internal",
        string targetType = "Host",
        IReadOnlyDictionary<string, IReadOnlyList<int>>? ports = null) => new(
            "Database",
            "demo",
            targetType,
            targetValue,
            ["mysql"],
            ports,
            true,
            null);

    private sealed class DiscoveryConfigurationStub : IDiscoveryConfiguration
    {
        public int MaxCidrHosts => 256;
        public int MaxPortsPerTarget => 16;
    }

    private sealed class AllowlistRepository(params ScanScopeAllowlist[] entries) : IScanScopeRepository
    {
        private readonly List<ScanScopeAllowlist> _entries = entries.ToList();
        public Task<IReadOnlyList<ScanScopeAllowlist>> ListEnabledAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ScanScopeAllowlist>>(_entries.Where(item => item.Enabled).ToArray());
        public Task<IReadOnlyList<ScanScopeAllowlist>> ListAsync(string? environment = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ScanScopeAllowlist>>(_entries.Where(item => environment is null || item.EnvironmentTag == environment).ToArray());
        public Task<ScanScopeAllowlist?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(_entries.SingleOrDefault(item => item.Id == id));
        public Task AddAsync(ScanScopeAllowlist entry, CancellationToken cancellationToken = default) { _entries.Add(entry); return Task.CompletedTask; }
        public Task DeleteAsync(ScanScopeAllowlist entry, CancellationToken cancellationToken = default) { _entries.Remove(entry); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> IsAllowedAsync(string host, int port, string environment, DatabaseEngine engine, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
