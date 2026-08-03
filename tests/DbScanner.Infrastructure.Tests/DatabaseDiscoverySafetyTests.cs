using System.Net;
using System.Net.Sockets;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Discovery;
using Microsoft.Extensions.Options;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

public sealed class DatabaseDiscoverySafetyTests
{
    [Theory]
    [InlineData(3306, "MySQL", DatabaseEngine.MySql, true)]
    [InlineData(5432, "PostgreSQL", DatabaseEngine.PostgreSql, true)]
    [InlineData(1433, "Microsoft SQL Server", DatabaseEngine.MsSql, true)]
    [InlineData(1521, "Oracle Database Listener", DatabaseEngine.Oracle, true)]
    public void EngineDetectors_ClassifySupportedAndDetectionOnlyServices(
        int port,
        string service,
        DatabaseEngine engine,
        bool expectedSupported)
    {
        IDatabaseFingerprintDetector[] detectors =
        [
            new MySqlDiscoveryDetector(),
            new PostgreSqlDiscoveryDetector(),
            new MsSqlDiscoveryDetector(),
            new OracleDiscoveryDetector(),
            new UnknownTcpDiscoveryDetector()
        ];
        var probe = new DiscoveryProbeResult("demo", port, true, []);
        var fingerprint = detectors.OrderBy(item => item.Order)
            .Select(item => item.Detect(probe))
            .First(item => item is not null)!;
        var classification = new DiscoveryResultClassifier().Classify(
            fingerprint,
            new HashSet<DatabaseEngine>
            {
                DatabaseEngine.MySql,
                DatabaseEngine.PostgreSql,
                DatabaseEngine.MsSql,
                DatabaseEngine.Oracle
            });

        Assert.Equal(service, classification.Service);
        Assert.Equal(engine, classification.Engine);
        Assert.Equal(
            expectedSupported ? DiscoveryResultStatus.Supported : DiscoveryResultStatus.Unsupported,
            classification.Status);
    }

    [Fact]
    public async Task ScopeValidator_RejectsUnauthorizedScope()
    {
        var validator = CreateValidator([]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.ValidateAndPlanAsync(
                "10.0.0.15",
                new HashSet<DatabaseEngine> { DatabaseEngine.MySql }));

        Assert.Equal("Discovery scope is outside the approved allowlist.", exception.Message);
    }

    [Fact]
    public async Task ScopeValidator_RejectsWidePublicInternetCidrEvenWhenAllowlisted()
    {
        var validator = CreateValidator(
        [
            new ScanScopeAllowlist
            {
                ScopeValue = "8.8.8.0/24",
                ScopeType = ScanScopeType.Cidr,
                Port = 3306
            }
        ]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.ValidateAndPlanAsync(
                "8.8.8.0/24",
                new HashSet<DatabaseEngine> { DatabaseEngine.MySql }));

        Assert.Equal("Wide public internet ranges are not permitted for discovery.", exception.Message);
    }

    [Fact]
    public async Task ScopeValidator_ExpandsOnlyAllowlistedHostPortPairs()
    {
        var validator = CreateValidator(
        [
            new ScanScopeAllowlist
            {
                ScopeValue = "192.168.1.0/30",
                ScopeType = ScanScopeType.Cidr,
                Port = 5432
            }
        ]);

        var endpoints = await validator.ValidateAndPlanAsync(
            "192.168.1.0/30",
            new HashSet<DatabaseEngine> { DatabaseEngine.MySql, DatabaseEngine.PostgreSql });

        Assert.Equal(
            [new DiscoveryEndpoint("192.168.1.1", 5432), new DiscoveryEndpoint("192.168.1.2", 5432)],
            endpoints);
    }

    [Fact]
    public async Task ScopeValidator_PlansAllowlistedOracleEndpointAsSupported()
    {
        var validator = CreateValidator(
        [
            new ScanScopeAllowlist
            {
                ScopeValue = "demo-oracle-target",
                ScopeType = ScanScopeType.DemoTarget,
                Port = 1521
            }
        ]);

        var endpoints = await validator.ValidateAndPlanAsync(
            "demo-oracle-target",
            new HashSet<DatabaseEngine> { DatabaseEngine.Oracle });

        Assert.Equal([new DiscoveryEndpoint("demo-oracle-target", 1521)], endpoints);
    }

    [Fact]
    public async Task TcpProbe_SendsNoPayloadOrCredentialAttempt()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var options = Options.Create(new DiscoveryOptions
            {
                ConnectTimeoutMilliseconds = 500,
                BannerReadTimeoutMilliseconds = 150,
                MaxBannerBytes = 64
            });
            var probeTask = new TcpDiscoveryProbe(options).ProbeAsync(new DiscoveryEndpoint("127.0.0.1", port));
            using var client = await listener.AcceptTcpClientAsync();
            var receiveBuffer = new byte[1];
            using var receiveTimeout = new CancellationTokenSource(100);
            var receivedBytes = 0;
            try
            {
                receivedBytes = await client.GetStream().ReadAsync(receiveBuffer, receiveTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected: the discovery probe passively waits for a server banner and writes nothing.
            }

            var result = await probeTask;
            Assert.True(result.IsOpen);
            Assert.Equal(0, receivedBytes);
            Assert.Empty(result.Banner);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static DiscoveryScopeValidator CreateValidator(IReadOnlyList<ScanScopeAllowlist> allowlist) =>
        new(
            new StubScopeRepository(allowlist),
            Options.Create(new DiscoveryOptions { MaxHosts = 256 }));

    private sealed class StubScopeRepository(IReadOnlyList<ScanScopeAllowlist> allowlist) : IScanScopeRepository
    {
        public Task<IReadOnlyList<ScanScopeAllowlist>> ListEnabledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(allowlist);

        public Task<bool> IsAllowedAsync(string host, int port, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
