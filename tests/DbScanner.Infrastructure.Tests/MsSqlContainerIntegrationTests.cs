using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using DbScanner.Application.Models;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Discovery;
using DbScanner.Infrastructure.Scanning;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.MsSql;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

[Collection(DatabaseContainerTestCollection.Name)]
public sealed class MsSqlContainerIntegrationTests
{
    private const string Image =
        "mcr.microsoft.com/mssql/server:2022-CU22-GDR1-ubuntu-22.04";

    [DockerFact]
    public async Task DiscoveryProbe_ConfirmsRealSqlServerWithoutCredentials()
    {
        await using var container = new MsSqlBuilder(Image)
            .WithPassword(CreatePassword())
            .WithEnvironment("MSSQL_PID", "Developer")
            .Build();
        await container.StartAsync();

        var probe = new TcpDiscoveryProbe(Options.Create(new DiscoveryOptions
        {
            ConnectTimeoutMilliseconds = 2000,
            BannerReadTimeoutMilliseconds = 2000,
            MaxBannerBytes = 256
        }));
        var result = await probe.ProbeAsync(new DiscoveryEndpoint(
            container.Hostname,
            container.GetMappedPublicPort(1433),
            DatabaseEngine.MsSql));
        var fingerprint = new MsSqlDiscoveryDetector().Detect(result);

        Assert.Equal(TcpDiscoveryProbe.MsSqlPreLoginEvidence, result.ProtocolEvidence);
        Assert.Equal(DatabaseEngine.MsSql, fingerprint!.Engine);
        Assert.Equal(DiscoveryFingerprintLevel.Confirmed, fingerprint.Level);
    }

    [DockerFact]
    public async Task AuthenticatedProbe_ExercisesRealSqlServerSafely()
    {
        var saPassword = CreatePassword();
        var appPassword = CreatePassword();
        var scannerPassword = CreatePassword();
        var limitedPassword = CreatePassword();
        await using var container = new MsSqlBuilder(Image)
            .WithPassword(saPassword)
            .WithEnvironment("MSSQL_PID", "Developer")
            .Build();

        await container.StartAsync();
        await InitializeFromDemoScriptAsync(
            container,
            appPassword,
            scannerPassword,
            limitedPassword);
        var passwordMatches = await ExecuteScalarAsync<int>(
            new SqlConnectionStringBuilder(container.GetConnectionString())
            {
                InitialCatalog = "master"
            }.ConnectionString,
            $"""
            SELECT CASE
                WHEN PWDCOMPARE(N'{Escape(scannerPassword)}', password_hash) = 1 THEN 1
                WHEN PWDCOMPARE(
                    N'{Convert.ToBase64String(Encoding.UTF8.GetBytes(scannerPassword))}',
                    password_hash) = 1 THEN 2
                WHEN PWDCOMPARE(N'{Escape(appPassword)}', password_hash) = 1 THEN 3
                ELSE 0
            END
            FROM sys.sql_logins
            WHERE name = N'dbscanner_scanner';
            """);
        Assert.Equal(1, passwordMatches);

        var connectionBuilder = new SqlConnectionStringBuilder(container.GetConnectionString());
        var target = new Target
        {
            Name = "Testcontainers Microsoft SQL Server",
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(1433),
            Engine = DatabaseEngine.MsSql
        };

        using (var tcpClient = new TcpClient())
        {
            await tcpClient.ConnectAsync(target.Host, target.Port);
            Assert.True(tcpClient.Connected);
        }

        var logger = new RecordingLogger<MsSqlAuthenticatedProbe>();
        var probe = new MsSqlAuthenticatedProbe(logger);
        var scannerAuthentication = new DatabaseAuthentication(
            "DbScannerDemo",
            "dbscanner_scanner",
            scannerPassword,
            Encrypt: true,
            TrustServerCertificate: true,
            ConnectionTimeoutSeconds: 10);

        var baselineChecks = new[]
        {
            CreateScript(MsSqlProbeScripts.Version),
            CreateScript(MsSqlProbeScripts.CurrentLoginSysadmin),
            CreateScript(MsSqlProbeScripts.XpCmdShell)
        };
        var baselineResults = await probe.RunAsync(target, baselineChecks, scannerAuthentication);
        var strategy = new MsSqlNativeScanExecutorStrategy(probe, new PassingBrowserProbe());
        var strategyResult = await strategy.ExecuteAsync(
            new ScanExecutionContext(new Scan { TargetId = target.Id, Target = target },
                target, scannerAuthentication),
            baselineChecks.Select(script => script.ToScanCheckDefinition(ScanExecutorTypes.MsSqlNative)).ToArray());
        Assert.Equal(
            baselineResults.Select(result => (result.ScriptName, result.NormalizedOutput, result.ResultStatus, (int?)result.ExitCode)),
            strategyResult.CheckResults.Select(result => (result.CheckName, result.NormalizedOutput, result.ResultStatus, result.ExitCode)));

        var versionResult = ResultFor(baselineResults, MsSqlProbeScripts.Version);
        Assert.True(
            versionResult.ResultStatus == ProbeResultStatus.Passed,
            $"{versionResult.NormalizedOutput}{Environment.NewLine}{string.Join(Environment.NewLine, logger.Messages)}");
        Assert.Contains(
            "Microsoft SQL Server",
            versionResult.NormalizedOutput);
        Assert.Equal(
            ProbeResultStatus.Passed,
            ResultFor(baselineResults, MsSqlProbeScripts.CurrentLoginSysadmin).ResultStatus);
        Assert.Equal(
            ProbeResultStatus.Passed,
            ResultFor(baselineResults, MsSqlProbeScripts.XpCmdShell).ResultStatus);

        await ExecuteAsync(
            container.GetConnectionString(),
            """
            ALTER DATABASE [DbScannerDemo] SET TRUSTWORTHY ON;
            USE [DbScannerDemo];
            GRANT CONNECT TO guest;
            """);

        var riskyResults = await probe.RunAsync(
            target,
            [
                CreateScript(MsSqlProbeScripts.Trustworthy),
                CreateScript(MsSqlProbeScripts.GuestAccess)
            ],
            scannerAuthentication);

        Assert.All(riskyResults, result => Assert.Equal(ProbeResultStatus.Failed, result.ResultStatus));

        var invalidPassword = $"invalid-{scannerPassword}";
        var invalidResults = await probe.RunAsync(
            target,
            [CreateScript(MsSqlProbeScripts.Version)],
            scannerAuthentication with { Password = invalidPassword });
        var invalidResult = Assert.Single(invalidResults);

        Assert.Equal(ProbeResultStatus.AuthenticationFailed, invalidResult.ResultStatus);
        Assert.Contains("authentication_failed", invalidResult.NormalizedOutput);
        Assert.DoesNotContain(scannerPassword, invalidResult.NormalizedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(invalidPassword, invalidResult.NormalizedOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(scannerPassword, string.Join(Environment.NewLine, logger.Messages));
        Assert.DoesNotContain(invalidPassword, string.Join(Environment.NewLine, logger.Messages));

        var limitedResults = await probe.RunAsync(
            target,
            [CreateScript(MsSqlProbeScripts.BackupEncryption)],
            new DatabaseAuthentication(
                "DbScannerDemo",
                "dbscanner_limited",
                limitedPassword,
                Encrypt: true,
                TrustServerCertificate: true,
                ConnectionTimeoutSeconds: 10));

        Assert.True(
            Assert.Single(limitedResults).ResultStatus
                is ProbeResultStatus.InsufficientPrivileges or ProbeResultStatus.Skipped);
        Assert.Equal("mssql", target.Engine.ToApiString());
        Assert.Equal("Microsoft SQL Server", target.Engine.ToDisplayName());

        connectionBuilder.InitialCatalog = "DbScannerDemo";
        await using var healthConnection = new SqlConnection(connectionBuilder.ConnectionString);
        await healthConnection.OpenAsync();
        await using var healthCommand = healthConnection.CreateCommand();
        healthCommand.CommandText = "SELECT COUNT_BIG(*) FROM dbo.SampleAssets;";
        Assert.Equal(2L, Convert.ToInt64(await healthCommand.ExecuteScalarAsync()));
    }

    [DockerAndNmapFact]
    public async Task Nmap_DetectsAndNormalizesRealSqlServerService()
    {
        await using var container = new MsSqlBuilder(Image)
            .WithPassword(CreatePassword())
            .WithEnvironment("MSSQL_PID", "Developer")
            .Build();
        await container.StartAsync();

        var target = new Target
        {
            Name = "Testcontainers Microsoft SQL Server",
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(1433),
            Engine = DatabaseEngine.MsSql
        };
        var scan = new Scan { TargetId = target.Id, Target = target };
        var script = CreateScript("ms-sql-info");
        var runtime = new MsSqlScannerRuntime(
            new ScriptCompatibilityScannerRuntime(
                new NmapScannerRuntime(
                    Options.Create(new ScannerOptions
                    {
                        ExecutablePath = "nmap",
                        TimeoutSeconds = 45,
                        UseSampleOutputWhenUnavailable = false
                    }),
                    new StubMySqlEmptyPasswordProbe(),
                    NullLogger<NmapScannerRuntime>.Instance)),
            new FailingAuthenticatedProbe(),
            new PassingBrowserProbe());

        var result = await runtime.RunAsync(scan, target, [script]);
        var scriptResult = Assert.Single(result.ScriptResults);

        Assert.Equal("ms-sql-info", scriptResult.ScriptName);
        Assert.Contains("Microsoft SQL Server", scriptResult.NormalizedOutput);
    }

    private static async Task InitializeFromDemoScriptAsync(
        MsSqlContainer container,
        string appPassword,
        string scannerPassword,
        string limitedPassword)
    {
        var scriptPath = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "docker",
                "mssql-init",
                "001-initialize-dbscanner-demo.sql"));
        var script = (await File.ReadAllTextAsync(scriptPath))
            .Replace(
                "$(DemoAppPasswordBase64)",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(appPassword)),
                StringComparison.Ordinal)
            .Replace(
                "$(DemoScannerPasswordBase64)",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(scannerPassword)),
                StringComparison.Ordinal)
            .Replace("$(EnableRiskySettings)", "false", StringComparison.Ordinal);

        var connectionString = container.GetConnectionString();
        var masterConnection = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master"
        }.ConnectionString;
        await ExecuteSqlScriptAsync(masterConnection, script);
        Assert.Equal(
            1,
            await PasswordMatchesAsync(
                masterConnection,
                "dbscanner_scanner",
                scannerPassword));
        await ExecuteSqlScriptAsync(masterConnection, script);
        Assert.Equal(
            1,
            await PasswordMatchesAsync(
                masterConnection,
                "dbscanner_scanner",
                scannerPassword));

        await ExecuteAsync(
            masterConnection,
            $"""
            CREATE LOGIN [dbscanner_limited]
              WITH PASSWORD = N'{Escape(limitedPassword)}', CHECK_POLICY = ON, CHECK_EXPIRATION = ON;
            """);

        var demoConnection = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "DbScannerDemo"
        }.ConnectionString;
        await ExecuteAsync(
            demoConnection,
            """
            CREATE USER [dbscanner_limited] FOR LOGIN [dbscanner_limited];
            GRANT CONNECT TO [dbscanner_limited];
            """);
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteSqlScriptAsync(
        string connectionString,
        string script)
    {
        var batches = Regex.Split(
            script,
            @"^\s*GO\s*;?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        foreach (var batch in batches.Where(
                     batch => !string.IsNullOrWhiteSpace(batch)))
        {
            await ExecuteAsync(connectionString, batch);
        }
    }

    private static async Task<T> ExecuteScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 30;
        return (T)Convert.ChangeType(await command.ExecuteScalarAsync() ?? default(T)!, typeof(T));
    }

    private static Task<int> PasswordMatchesAsync(
        string connectionString,
        string loginName,
        string password) =>
        ExecuteScalarAsync<int>(
            connectionString,
            $"""
            SELECT CONVERT(int, PWDCOMPARE(
                N'{Escape(password)}',
                password_hash))
            FROM sys.sql_logins
            WHERE name = N'{Escape(loginName)}';
            """);

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string CreatePassword() => $"DbScanner_{Guid.NewGuid():N}_Aa1!";

    private static NseScript CreateScript(string name) =>
        new()
        {
            Id = $"integration-{name}",
            NseScriptName = name,
            DisplayName = name,
            Description = name,
            SupportedEngines = "mssql",
            DefaultEnabled = true,
            ParserType = ParserType.GenericOutput,
            MatcherType = MatcherType.Contains,
            ApprovalStatus = ScriptApprovalStatus.Approved,
            RequiresCredentials = MsSqlProbeScripts.IsAuthenticatedProbe(name)
        };

    private static ScanScriptExecutionResult ResultFor(
        IReadOnlyList<ScanScriptExecutionResult> results,
        string scriptName) =>
        Assert.Single(results, result => result.ScriptName == scriptName);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class StubMySqlEmptyPasswordProbe : IMySqlEmptyPasswordProbe
    {
        public Task<ScanScriptExecutionResult> RunAsync(
            Target target,
            NseScript script,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScanScriptExecutionResult(
                script.Id,
                script.NseScriptName,
                null,
                "status=skipped",
                0,
                ProbeResultStatus.Skipped));
    }

    private sealed class FailingAuthenticatedProbe : IMsSqlAuthenticatedProbe
    {
        public Task<IReadOnlyList<ScanScriptExecutionResult>> RunAsync(
            Target target,
            IReadOnlyCollection<NseScript> scripts,
            DatabaseAuthentication authentication,
            CancellationToken cancellationToken = default) =>
            throw new Xunit.Sdk.XunitException("Authenticated probe was not expected.");
    }

    private sealed class PassingBrowserProbe : IMsSqlBrowserProbe
    {
        public Task<ScanScriptExecutionResult> RunAsync(
            Target target,
            NseScript script,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScanScriptExecutionResult(
                script.Id,
                script.NseScriptName,
                null,
                "status=passed",
                0,
                ProbeResultStatus.Passed));
    }
}
