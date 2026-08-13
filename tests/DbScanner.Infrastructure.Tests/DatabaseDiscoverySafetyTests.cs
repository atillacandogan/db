using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Contracts.Discovery;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Discovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

public sealed class DatabaseDiscoverySafetyTests
{
    private static readonly HashSet<DatabaseEngine> MySqlOnly = [DatabaseEngine.MySql];

    [Fact]
    public async Task ScopeValidator_LegacyAuthorizedPlanFiltersEnvironmentAndActiveState()
    {
        var validator = CreateValidator([
            Entry("10.0.0.15", ScanScopeType.IpAddress, "demo", true, "mysql:3306"),
            Entry("10.0.0.15", ScanScopeType.IpAddress, "prod", false, "mysql:3306")]);

        var demo = await validator.PlanAuthorizedAsync("demo", MySqlOnly);

        Assert.NotEmpty(demo);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.PlanAuthorizedAsync("prod", MySqlOnly));
    }

    [Fact]
    public async Task ScopeValidator_AuthorizesIpContainedByCidr()
    {
        var validator = CreateValidator([]);

        var plan = await validator.ValidateAndPlanDetailedAsync("10.10.0.42", "demo", MySqlOnly);

        Assert.True(plan.IsValid);
        Assert.Contains(plan.Endpoints, endpoint => endpoint.Host == "10.10.0.42" && endpoint.Port == 3306);
    }

    [Fact]
    public async Task ScopeValidator_PlansDetectedPrivateNetworkWithoutAllowlist()
    {
        var validator = CreateValidator([]);

        var endpoints = await validator.PlanLocalNetworksAsync(["192.168.50.0/30"], MySqlOnly);

        Assert.Equal(8, endpoints.Count);
        Assert.All(endpoints.Where(endpoint => endpoint.ExpectedEngine is not null), endpoint =>
        {
            Assert.Contains(endpoint.Port, new[] { 3306, 13306 });
            Assert.Equal(DatabaseEngine.MySql, endpoint.ExpectedEngine);
            Assert.StartsWith("192.168.50.", endpoint.Host);
        });
        Assert.All(endpoints.Where(endpoint => endpoint.ExpectedEngine is null), endpoint =>
        {
            Assert.Contains(endpoint.Port, new[] { 6379, 27017 });
            Assert.StartsWith("192.168.50.", endpoint.Host);
        });
    }

    [Theory]
    [InlineData("8.8.8.0/30")]
    [InlineData("10.0.0.0/8")]
    public async Task ScopeValidator_RejectsUnsafeOrOversizedDetectedNetwork(string cidr)
    {
        var validator = CreateValidator([], maxCidrHosts: 256);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.PlanLocalNetworksAsync([cidr], MySqlOnly));
    }

    [Theory]
    [InlineData("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter")]
    [InlineData("DockerNAT", "Docker Desktop Network")]
    [InlineData("Company VPN", "WireGuard Tunnel")]
    public void LocalNetworkProvider_ExcludesVirtualContainerAndVpnAdapters(string name, string description)
    {
        Assert.True(LocalNetworkProvider.IsExcludedInterface(
            name,
            description,
            NetworkInterfaceType.Ethernet));
    }

    [Fact]
    public void LocalNetworkProvider_ReturnsOnlyPrivateIpv4Ranges()
    {
        var provider = new LocalNetworkProvider(Options.Create(new DiscoveryOptions()));

        var ranges = provider.GetPrivateIpv4Networks();

        Assert.All(ranges, range =>
        {
            Assert.True(NetworkScope.TryParseIpv4Cidr(range.Cidr, out var network, out _));
            Assert.True(NetworkScope.IsPrivateNetwork(network));
        });
    }

    [Fact]
    public async Task ScopeValidator_ReturnsEveryMixedTargetResultAndKeepsAuthorizedEndpoints()
    {
        var validator = CreateValidator([]);

        var plan = await validator.ValidateAndPlanDetailedAsync(
            "10.0.0.15,10.0.0.99,not@host",
            "demo",
            MySqlOnly);

        Assert.True(plan.IsValid);
        Assert.NotEmpty(plan.Endpoints);
        Assert.Equal(
            ["10.0.0.15", "10.0.0.99"],
            plan.Endpoints.Select(endpoint => endpoint.Host).Distinct().Order().ToArray());
        Assert.Collection(
            plan.Targets,
            item => Assert.Equal(DiscoveryTargetValidationStatus.Authorized, item.Status),
            item => Assert.Equal(DiscoveryTargetValidationStatus.Authorized, item.Status),
            item => Assert.Equal(DiscoveryTargetValidationStatus.InvalidFormat, item.Status));

        var executableEndpoints = await validator.ValidateAndPlanAsync(
            "10.0.0.15,10.0.0.99,not@host",
            "demo",
            MySqlOnly);

        Assert.Equal(
            ["10.0.0.15", "10.0.0.99"],
            executableEndpoints.Select(endpoint => endpoint.Host).Distinct().Order().ToArray());
    }

    [Fact]
    public async Task ScopeValidator_RejectsMixedScopeWhenNoTargetIsAuthorized()
    {
        var validator = CreateValidator([]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.ValidateAndPlanAsync("not@host,2001:db8::1", "demo", MySqlOnly));

        Assert.Contains("Discovery scope validation failed", exception.Message);
    }

    [Fact]
    public async Task ScopeValidator_PinsEveryAuthorizedDnsAddress()
    {
        var addresses = new[] { IPAddress.Parse("10.0.0.20"), IPAddress.Parse("10.0.0.21") };
        var validator = CreateValidator(
            [],
            new StubDnsResolver(new Dictionary<string, IReadOnlyList<IPAddress>> { ["db.internal"] = addresses }));

        var plan = await validator.ValidateAndPlanDetailedAsync("db.internal", "demo", MySqlOnly);

        Assert.True(plan.IsValid);
        Assert.Equal(
            ["10.0.0.20", "10.0.0.21"],
            plan.Endpoints.Select(item => item.ConnectAddress!).Distinct().Order().ToArray());
        Assert.All(plan.Endpoints, endpoint => Assert.Equal("db.internal", endpoint.Host));
    }

    [Fact]
    public async Task ScopeValidator_RejectsHostnameResolvingToPublicAddress()
    {
        var publicValidator = CreateValidator(
            [],
            new StubDnsResolver(new Dictionary<string, IReadOnlyList<IPAddress>>
            {
                ["db.internal"] = [IPAddress.Parse("10.0.0.20"), IPAddress.Parse("8.8.8.8")]
            }));
        var publicPlan = await publicValidator.ValidateAndPlanDetailedAsync("db.internal", "demo", MySqlOnly);

        Assert.Equal(DiscoveryTargetValidationStatus.ResolvedAddressNotAuthorized, publicPlan.Targets.Single().Status);
        Assert.Empty(publicPlan.Endpoints);
    }

    [Fact]
    public async Task ScopeValidator_RejectsLargeCidrBeforeExpansion()
    {
        var validator = CreateValidator([], maxCidrHosts: 256);

        var plan = await validator.ValidateAndPlanDetailedAsync("10.0.0.0/8", "demo", MySqlOnly);

        Assert.Equal(DiscoveryTargetValidationStatus.CidrTooLarge, plan.Targets.Single().Status);
        Assert.Empty(plan.Endpoints);
    }

    [Fact]
    public async Task ScopeValidator_UsesCustomPortsOnlyForSelectedEngines()
    {
        var validator = CreateValidator([]);

        var plan = await validator.ValidateAndPlanDetailedAsync("10.0.0.15", "demo", MySqlOnly);

        Assert.Equal([3306, 6379, 13306, 27017], plan.Endpoints.Select(endpoint => endpoint.Port).Order().ToArray());
        Assert.Equal(
            [3306, 13306],
            plan.Endpoints.Where(endpoint => endpoint.ExpectedEngine == DatabaseEngine.MySql)
                .Select(endpoint => endpoint.Port).Order().ToArray());
        Assert.Equal(
            [6379, 27017],
            plan.Endpoints.Where(endpoint => endpoint.ExpectedEngine is null)
                .Select(endpoint => endpoint.Port).Order().ToArray());
    }

    [Fact]
    public async Task ScopeValidator_RejectsIpv6Explicitly()
    {
        var validator = CreateValidator([]);

        var plan = await validator.ValidateAndPlanDetailedAsync("2001:db8::1", "demo", MySqlOnly);

        Assert.Equal(DiscoveryTargetValidationStatus.UnsupportedAddressType, plan.Targets.Single().Status);
        Assert.Equal("IPv6 discovery is not currently supported.", plan.Targets.Single().Message);
    }

    [Theory]
    [InlineData(DatabaseEngine.MySql, 13306, TcpDiscoveryProbe.MySqlHandshakeEvidence, "MySQL")]
    [InlineData(DatabaseEngine.PostgreSql, 15432, TcpDiscoveryProbe.PostgreSqlSslEvidence, "PostgreSQL")]
    [InlineData(DatabaseEngine.MsSql, 11433, TcpDiscoveryProbe.MsSqlPreLoginEvidence, "Microsoft SQL Server")]
    [InlineData(DatabaseEngine.Oracle, 11521, TcpDiscoveryProbe.OracleTnsEvidence, "Oracle Database Listener")]
    public void ProtocolEvidence_ConfirmsEngineOnCustomPort(
        DatabaseEngine engine,
        int port,
        string evidence,
        string expectedService)
    {
        IDatabaseFingerprintDetector detector = engine switch
        {
            DatabaseEngine.MySql => new MySqlDiscoveryDetector(),
            DatabaseEngine.PostgreSql => new PostgreSqlDiscoveryDetector(),
            DatabaseEngine.MsSql => new MsSqlDiscoveryDetector(),
            _ => new OracleDiscoveryDetector()
        };

        var fingerprint = detector.Detect(new DiscoveryProbeResult("db", port, true, [], engine, evidence));

        Assert.NotNull(fingerprint);
        Assert.Equal(engine, fingerprint.Engine);
        Assert.Equal(expectedService, fingerprint.Service);
        Assert.Equal(DiscoveryFingerprintLevel.Confirmed, fingerprint.Level);
    }

    [Theory]
    [InlineData(DatabaseEngine.MySql, 3306)]
    [InlineData(DatabaseEngine.PostgreSql, 5432)]
    [InlineData(DatabaseEngine.MsSql, 1433)]
    [InlineData(DatabaseEngine.Oracle, 1521)]
    public void OpenPortWithoutProtocolEvidence_DoesNotClaimEngine(DatabaseEngine engine, int port)
    {
        IDatabaseFingerprintDetector detector = engine switch
        {
            DatabaseEngine.MySql => new MySqlDiscoveryDetector(),
            DatabaseEngine.PostgreSql => new PostgreSqlDiscoveryDetector(),
            DatabaseEngine.MsSql => new MsSqlDiscoveryDetector(),
            _ => new OracleDiscoveryDetector()
        };

        var fingerprint = detector.Detect(new DiscoveryProbeResult("db", port, true, [], engine));

        Assert.NotNull(fingerprint);
        Assert.Null(fingerprint.Engine);
        Assert.Equal(DiscoveryFingerprintLevel.PortHint, fingerprint.Level);
    }

    [Fact]
    public void ProtocolResponseValidators_RejectMalformedResponses()
    {
        var validTdsResponse = CreateTdsPreLoginResponse();
        var wrongTdsPacketType = validTdsResponse.ToArray();
        wrongTdsPacketType[0] = 0x12;
        var wrongTdsPacketLength = validTdsResponse.ToArray();
        wrongTdsPacketLength[3]--;
        var invalidTdsEncryption = validTdsResponse.ToArray();
        invalidTdsEncryption[35] = 0x7F;
        var outOfBoundsTdsToken = validTdsResponse.ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(outOfBoundsTdsToken.AsSpan(9, 2), 0xFFFF);
        var overlappingTdsTokens = validTdsResponse.ToArray();
        BinaryPrimitives.WriteUInt16BigEndian(overlappingTdsTokens.AsSpan(14, 2), 21);
        var missingTdsTerminator = validTdsResponse.ToArray();
        missingTdsTerminator[28] = 0x08;
        var versionIsNotFirst = validTdsResponse.ToArray();
        versionIsNotFirst[8] = 0x08;

        Assert.True(TcpDiscoveryProbe.IsMySqlHandshakeResponse(CreateMySqlHandshake()));
        Assert.False(TcpDiscoveryProbe.IsMySqlHandshakeResponse([1, 0, 0, 1, 10]));
        Assert.True(TcpDiscoveryProbe.IsPostgreSqlSslResponse([(byte)'S']));
        Assert.True(TcpDiscoveryProbe.IsPostgreSqlSslResponse([(byte)'N']));
        Assert.False(TcpDiscoveryProbe.IsPostgreSqlSslResponse([(byte)'X']));
        Assert.True(TcpDiscoveryProbe.IsMsSqlPreLoginResponse(validTdsResponse));
        Assert.False(TcpDiscoveryProbe.IsMsSqlPreLoginResponse(wrongTdsPacketType));
        Assert.False(TcpDiscoveryProbe.IsMsSqlPreLoginResponse(wrongTdsPacketLength));
        Assert.False(TcpDiscoveryProbe.IsMsSqlPreLoginResponse(invalidTdsEncryption));
        Assert.False(TcpDiscoveryProbe.IsMsSqlPreLoginResponse(outOfBoundsTdsToken));
        Assert.False(TcpDiscoveryProbe.IsMsSqlPreLoginResponse(overlappingTdsTokens));
        Assert.False(TcpDiscoveryProbe.IsMsSqlPreLoginResponse(missingTdsTerminator));
        Assert.False(TcpDiscoveryProbe.IsMsSqlPreLoginResponse(versionIsNotFirst));
        Assert.False(TcpDiscoveryProbe.IsMsSqlPreLoginResponse([0x04, 0x01, 0, 8, 0, 0, 0, 0]));
        Assert.True(TcpDiscoveryProbe.IsOracleTnsResponse([0, 8, 0, 0, 4, 0, 0, 0]));
        Assert.False(TcpDiscoveryProbe.IsOracleTnsResponse([0, 8, 0, 0, 1, 0, 0, 0]));
    }

    [Fact]
    public void TdsPreLoginRequest_ComputesHeaderLengthOffsetsAndMarsValue()
    {
        var request = TdsPreLoginProtocol.CreateRequest();

        Assert.Equal(47, request.Length);
        Assert.Equal(0x12, request[0]);
        Assert.Equal(0x01, request[1]);
        Assert.Equal(request.Length, BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(2, 2)));
        Assert.Equal(0x00, request[8]);
        Assert.Equal(0x04, request[28]);
        Assert.Equal(38, BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(29, 2)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(31, 2)));
        Assert.Equal(0xFF, request[33]);
        Assert.Equal(0x00, request[46]);
    }

    [Fact]
    public void TdsPreLoginResponseParser_AcceptsStructurallyValidUnknownExtensionToken()
    {
        var response = CreateTdsPreLoginResponse(
        [
            (0x00, new byte[] { 0x10, 0x00, 0x06, 0xA4, 0x00, 0x00 }),
            (0x01, new byte[] { 0x03 }),
            (0x08, new byte[] { 0xAA, 0x55 }),
            (0x04, new byte[] { 0x01 })
        ]);

        Assert.True(TcpDiscoveryProbe.IsMsSqlPreLoginResponse(response));
    }

    [Fact]
    public async Task TcpProbe_CombinesFragmentedMssqlPreLoginResponse()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var probeTask = new TcpDiscoveryProbe(Options.Create(new DiscoveryOptions
            {
                ConnectTimeoutMilliseconds = 500,
                BannerReadTimeoutMilliseconds = 500,
                MaxBannerBytes = 64
            })).ProbeAsync(new DiscoveryEndpoint("127.0.0.1", port, DatabaseEngine.MsSql));

            using var client = await listener.AcceptTcpClientAsync();
            using var timeout = new CancellationTokenSource(2000);
            var request = await ReadTdsPacketAsync(client.GetStream(), timeout.Token);
            Assert.Equal(TdsPreLoginProtocol.CreateRequest(), request);

            var response = CreateTdsPreLoginResponse();
            await client.GetStream().WriteAsync(response.AsMemory(0, 8), timeout.Token);
            await Task.Delay(25, timeout.Token);
            await client.GetStream().WriteAsync(response.AsMemory(8), timeout.Token);

            var result = await probeTask;

            Assert.True(result.IsOpen);
            Assert.Equal(TcpDiscoveryProbe.MsSqlPreLoginEvidence, result.ProtocolEvidence);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task TcpProbe_RetriesMssqlOnceOnIncompleteResponseUsingCleanConnection()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var serverTask = Task.Run(async () =>
            {
                using var timeout = new CancellationTokenSource(3000);
                using (var firstAttempt = await listener.AcceptTcpClientAsync(timeout.Token))
                {
                    Assert.Equal(
                        TdsPreLoginProtocol.CreateRequest(),
                        await ReadTdsPacketAsync(firstAttempt.GetStream(), timeout.Token));
                }

                using var retryAttempt = await listener.AcceptTcpClientAsync(timeout.Token);
                Assert.Equal(
                    TdsPreLoginProtocol.CreateRequest(),
                    await ReadTdsPacketAsync(retryAttempt.GetStream(), timeout.Token));
                await retryAttempt.GetStream().WriteAsync(CreateTdsPreLoginResponse(), timeout.Token);
            });

            var result = await CreateProbe().ProbeAsync(
                new DiscoveryEndpoint("127.0.0.1", port, DatabaseEngine.MsSql));
            await serverTask;

            Assert.Equal(TcpDiscoveryProbe.MsSqlPreLoginEvidence, result.ProtocolEvidence);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Theory]
    [InlineData(6379, "Redis")]
    [InlineData(27017, "MongoDB")]
    public void UnsupportedDatabasePorts_AreDetectedAndClassifiedForDisplay(int port, string service)
    {
        IDatabaseFingerprintDetector detector = port == 6379
            ? new RedisDiscoveryDetector()
            : new MongoDbDiscoveryDetector();

        var fingerprint = detector.Detect(new DiscoveryProbeResult("db", port, true, []));
        var classification = new DiscoveryResultClassifier().Classify(fingerprint!, MySqlOnly);

        Assert.Equal(service, classification.Service);
        Assert.Equal(DiscoveryResultStatus.Unsupported, classification.Status);
        Assert.Equal(DiscoveryFingerprintLevel.PortHint, classification.FingerprintLevel);
        Assert.Contains("not confirmed", classification.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MySqlAndPostgreSqlRequireProtocolEvidenceToBeSupported()
    {
        var mysql = new MySqlDiscoveryDetector().Detect(new DiscoveryProbeResult(
            "mysql",
            13306,
            true,
            [],
            DatabaseEngine.MySql,
            TcpDiscoveryProbe.MySqlHandshakeEvidence));
        var postgres = new PostgreSqlDiscoveryDetector().Detect(new DiscoveryProbeResult(
            "postgres",
            15432,
            true,
            [],
            DatabaseEngine.PostgreSql,
            TcpDiscoveryProbe.PostgreSqlSslEvidence));

        Assert.Equal(DatabaseEngine.MySql, mysql!.Engine);
        Assert.Equal(DiscoveryFingerprintLevel.Confirmed, mysql.Level);
        Assert.Equal(DatabaseEngine.PostgreSql, postgres!.Engine);
        Assert.Equal(DiscoveryFingerprintLevel.Confirmed, postgres.Level);
    }

    [Fact]
    public void DiscoveryOptions_ValidatesAllSafetyLimits()
    {
        Assert.True(DiscoveryOptions.IsValid(new DiscoveryOptions()));
        Assert.False(DiscoveryOptions.IsValid(new DiscoveryOptions { MaxBannerBytes = 63 }));
        Assert.False(DiscoveryOptions.IsValid(new DiscoveryOptions { MaxCidrHosts = 5000 }));
        Assert.False(DiscoveryOptions.IsValid(new DiscoveryOptions { MaxPortsPerTarget = 65 }));
        Assert.False(DiscoveryOptions.IsValid(new DiscoveryOptions { ConnectTimeoutMilliseconds = 99 }));
        Assert.False(DiscoveryOptions.IsValid(new DiscoveryOptions { MaxConcurrency = 0 }));
        Assert.False(DiscoveryOptions.IsValid(new DiscoveryOptions { ProbeDelayMilliseconds = 10001 }));
        Assert.False(DiscoveryOptions.IsValid(new DiscoveryOptions
        {
            LocalNetworkPorts = new Dictionary<string, int[]> { ["mysql"] = [0] }
        }));
    }

    [Fact]
    public void DiscoveryOptions_AreBoundByFrameworkFromDiscoverySection()
    {
        var values = new Dictionary<string, string?>
        {
            ["Discovery:ConnectTimeoutMilliseconds"] = "1200",
            ["Discovery:BannerReadTimeoutMilliseconds"] = "250",
            ["Discovery:DnsTimeoutMilliseconds"] = "1800",
            ["Discovery:JobTimeoutSeconds"] = "240",
            ["Discovery:MaxConcurrency"] = "6",
            ["Discovery:MaxProbesPerSecond"] = "12",
            ["Discovery:ProbeDelayMilliseconds"] = "75",
            ["Discovery:MaxCidrHosts"] = "128",
            ["Discovery:MaxPortsPerTarget"] = "12",
            ["Discovery:MaxBannerBytes"] = "1024",
            ["Discovery:LocalNetworkPorts:mysql:0"] = "3306",
            ["Discovery:LocalNetworkPorts:mysql:1"] = "13306",
            ["Discovery:UnsupportedDatabasePorts:0"] = "6380"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var provider = new ServiceCollection().AddDbScannerInfrastructure(configuration).BuildServiceProvider();

        var bound = provider.GetRequiredService<IOptions<DiscoveryOptions>>().Value;

        Assert.Equal(1200, bound.ConnectTimeoutMilliseconds);
        Assert.Equal(250, bound.BannerReadTimeoutMilliseconds);
        Assert.Equal(1800, bound.DnsTimeoutMilliseconds);
        Assert.Equal(240, bound.JobTimeoutSeconds);
        Assert.Equal(6, bound.MaxConcurrency);
        Assert.Equal(12, bound.MaxProbesPerSecond);
        Assert.Equal(75, bound.ProbeDelayMilliseconds);
        Assert.Equal(128, bound.MaxCidrHosts);
        Assert.Equal(12, bound.MaxPortsPerTarget);
        Assert.Equal(1024, bound.MaxBannerBytes);
        Assert.Equal([3306, 13306], bound.LocalNetworkPorts["mysql"]);
        Assert.Equal([6380], bound.UnsupportedDatabasePorts);
    }

    [Fact]
    public async Task TcpProbe_PostgreSqlExpectedPortSendsCredentiallessSslRequest()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var probeTask = new TcpDiscoveryProbe(Options.Create(new DiscoveryOptions
            {
                ConnectTimeoutMilliseconds = 500,
                BannerReadTimeoutMilliseconds = 500,
                MaxBannerBytes = 64
            })).ProbeAsync(new DiscoveryEndpoint("127.0.0.1", port, DatabaseEngine.PostgreSql));
            using var client = await listener.AcceptTcpClientAsync();
            var buffer = new byte[8];
            using var timeout = new CancellationTokenSource(2000);
            await client.GetStream().ReadExactlyAsync(buffer, timeout.Token);
            Assert.Equal(new byte[] { 0, 0, 0, 8, 4, 210, 22, 47 }, buffer);
            await client.GetStream().WriteAsync(new byte[] { (byte)'N' }, timeout.Token);

            var result = await probeTask;
            Assert.True(result.IsOpen);
            Assert.Equal(TcpDiscoveryProbe.PostgreSqlSslEvidence, result.ProtocolEvidence);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task TcpProbe_DetectsMySqlOnPostgreSqlAssociatedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var handshake = CreateMySqlHandshake();
            var serverTask = Task.Run(async () =>
            {
                using var timeout = new CancellationTokenSource(3000);
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                    await client.GetStream().WriteAsync(handshake, timeout.Token);
                }
            });

            var result = await CreateProbe().ProbeAsync(
                new DiscoveryEndpoint("127.0.0.1", port, DatabaseEngine.PostgreSql));
            await serverTask;

            Assert.Equal(TcpDiscoveryProbe.MySqlHandshakeEvidence, result.ProtocolEvidence);
            var fingerprint = new MySqlDiscoveryDetector().Detect(result);
            Assert.Equal(DatabaseEngine.MySql, fingerprint!.Engine);
            Assert.Equal(DiscoveryFingerprintLevel.Confirmed, fingerprint.Level);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task TcpProbe_DetectsPostgreSqlOnMySqlAssociatedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var serverTask = Task.Run(async () =>
            {
                using var timeout = new CancellationTokenSource(3000);
                using (var mysqlAttempt = await listener.AcceptTcpClientAsync(timeout.Token))
                {
                    // A PostgreSQL server does not send a server-first MySQL greeting.
                }

                using var postgresAttempt = await listener.AcceptTcpClientAsync(timeout.Token);
                var request = new byte[8];
                await postgresAttempt.GetStream().ReadExactlyAsync(request, timeout.Token);
                Assert.Equal(new byte[] { 0, 0, 0, 8, 4, 210, 22, 47 }, request);
                await postgresAttempt.GetStream().WriteAsync(new byte[] { (byte)'N' }, timeout.Token);
            });

            var result = await CreateProbe().ProbeAsync(
                new DiscoveryEndpoint("127.0.0.1", port, DatabaseEngine.MySql));
            await serverTask;

            Assert.Equal(TcpDiscoveryProbe.PostgreSqlSslEvidence, result.ProtocolEvidence);
            var fingerprint = new PostgreSqlDiscoveryDetector().Detect(result);
            Assert.Equal(DatabaseEngine.PostgreSql, fingerprint!.Engine);
            Assert.Equal(DiscoveryFingerprintLevel.Confirmed, fingerprint.Level);
        }
        finally
        {
            listener.Stop();
        }
    }

    private static TcpDiscoveryProbe CreateProbe() => new(Options.Create(new DiscoveryOptions
    {
        ConnectTimeoutMilliseconds = 500,
        BannerReadTimeoutMilliseconds = 250,
        MaxBannerBytes = 64
    }));

    private static byte[] CreateMySqlHandshake() =>
    [
        28, 0, 0, 0,
        10,
        (byte)'8', (byte)'.', (byte)'4', (byte)'.', (byte)'0', 0,
        1, 0, 0, 0,
        1, 2, 3, 4, 5, 6, 7, 8,
        0,
        0xFF, 0xF7,
        0x21,
        0, 0,
        0x02, 0,
        0
    ];

    private static byte[] CreateTdsPreLoginResponse(
        IReadOnlyList<(byte Token, byte[] Data)>? suppliedOptions = null)
    {
        IReadOnlyList<(byte Token, byte[] Data)> options = suppliedOptions ??
        [
            (0x00, new byte[] { 0x0F, 0x00, 0x07, 0xD0, 0x00, 0x00 }),
            (0x01, new byte[] { 0x01 }),
            (0x02, new byte[] { 0x00 }),
            (0x04, new byte[] { 0x00 })
        ];
        var directoryLength = options.Count * 5 + 1;
        var packet = new byte[8 + directoryLength + options.Sum(option => option.Data.Length)];
        packet[0] = 0x04;
        packet[1] = 0x01;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), checked((ushort)packet.Length));
        packet[6] = 0x01;

        var directoryOffset = 8;
        var dataOffset = directoryLength;
        foreach (var option in options)
        {
            packet[directoryOffset] = option.Token;
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(directoryOffset + 1, 2), checked((ushort)dataOffset));
            BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(directoryOffset + 3, 2), checked((ushort)option.Data.Length));
            option.Data.CopyTo(packet, 8 + dataOffset);
            directoryOffset += 5;
            dataOffset += option.Data.Length;
        }

        packet[directoryOffset] = 0xFF;
        return packet;
    }

    private static async Task<byte[]> ReadTdsPacketAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var header = new byte[8];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var packetLength = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2, 2));
        var packet = new byte[packetLength];
        header.CopyTo(packet, 0);
        await stream.ReadExactlyAsync(packet.AsMemory(8), cancellationToken);
        return packet;
    }

    private static ScanScopeAllowlist Entry(
        string value,
        ScanScopeType type,
        string environment = "demo",
        bool enabled = true,
        string ports = "mysql:3306") => new()
        {
            Name = value,
            ScopeValue = value,
            ScopeType = type,
            EnvironmentTag = environment,
            AllowedEngines = ports.Split(';').Select(item => item.Split(':')[0]).Distinct().Aggregate((left, right) => $"{left},{right}"),
            AllowedPorts = ports,
            Enabled = enabled
        };

    private static DiscoveryScopeValidator CreateValidator(
        IReadOnlyList<ScanScopeAllowlist> allowlist,
        IDiscoveryDnsResolver? dnsResolver = null,
        int maxCidrHosts = 256) => new(
            new StubScopeRepository(allowlist),
            dnsResolver ?? new StubDnsResolver(new Dictionary<string, IReadOnlyList<IPAddress>>()),
            Options.Create(new DiscoveryOptions { MaxCidrHosts = maxCidrHosts }));

    private sealed class StubDnsResolver(IReadOnlyDictionary<string, IReadOnlyList<IPAddress>> addresses) : IDiscoveryDnsResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveIpv4Async(string hostname, CancellationToken cancellationToken = default) =>
            Task.FromResult(addresses.TryGetValue(hostname, out var result) ? result : (IReadOnlyList<IPAddress>)[]);
    }

    private sealed class StubScopeRepository(IReadOnlyList<ScanScopeAllowlist> allowlist) : IScanScopeRepository
    {
        public Task<IReadOnlyList<ScanScopeAllowlist>> ListEnabledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScanScopeAllowlist>>(allowlist.Where(entry => entry.Enabled).ToArray());

        public Task<IReadOnlyList<ScanScopeAllowlist>> ListAsync(string? environment = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScanScopeAllowlist>>(allowlist.Where(entry => environment is null || entry.EnvironmentTag == environment).ToArray());

        public Task<ScanScopeAllowlist?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(allowlist.SingleOrDefault(entry => entry.Id == id));

        public Task AddAsync(ScanScopeAllowlist entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(ScanScopeAllowlist entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> IsAllowedAsync(string host, int port, string environment, DatabaseEngine engine, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
