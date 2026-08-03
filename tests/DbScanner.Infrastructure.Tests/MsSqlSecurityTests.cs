using System.Security.Cryptography;
using DbScanner.Application.Models;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Scanning;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

public sealed class MsSqlSecurityTests
{
    [Fact]
    public void EngineMapping_UsesCanonicalApiValueAndDisplayName()
    {
        Assert.Equal("mssql", DatabaseEngine.MsSql.ToApiString());
        Assert.Equal("Microsoft SQL Server", DatabaseEngine.MsSql.ToDisplayName());
        Assert.True(DatabaseEngineExtensions.TryParseApiString("mssql", out var engine));
        Assert.Equal(DatabaseEngine.MsSql, engine);
        Assert.False(DatabaseEngineExtensions.TryParseApiString("sqlserver", out _));
    }

    [Fact]
    public void CredentialProtector_RoundTripsWithoutEmbeddingPlaintext()
    {
        var protector = CreateProtector();
        var authentication = new DatabaseAuthentication(
            "DbScannerDemo",
            "dbscanner_scanner",
            "Secret-that-must-not-leak!",
            Encrypt: true,
            TrustServerCertificate: false,
            ConnectionTimeoutSeconds: 7);

        var protectedCredentials = protector.Protect(authentication);
        var restored = protector.Unprotect(protectedCredentials);

        Assert.DoesNotContain(authentication.Password, protectedCredentials, StringComparison.Ordinal);
        Assert.Equal(authentication, restored);
    }

    [Fact]
    public void CredentialProtector_RejectsTamperedEnvelopeWithoutLeakingCredentials()
    {
        var protector = CreateProtector();
        var authentication = new DatabaseAuthentication(
            "DbScannerDemo",
            "dbscanner_scanner",
            "Secret-that-must-not-leak!");
        var envelope = Convert.FromBase64String(protector.Protect(authentication));
        envelope[^1] ^= 0x01;

        var exception = Assert.Throws<InvalidOperationException>(
            () => protector.Unprotect(Convert.ToBase64String(envelope)));

        Assert.DoesNotContain(authentication.Password, exception.Message, StringComparison.Ordinal);
        Assert.Equal("Protected scan credentials could not be decrypted.", exception.Message);
    }

    [Fact]
    public void ConnectionStringBuilder_UsesExplicitSecurityAndTimeoutSettings()
    {
        var target = new Target
        {
            Name = "Demo Microsoft SQL Server",
            Host = "demo-mssql-target",
            Port = 1433,
            Engine = DatabaseEngine.MsSql
        };
        var authentication = new DatabaseAuthentication(
            " DbScannerDemo ",
            " dbscanner_scanner ",
            "P@ss;word=still-safe",
            Encrypt: true,
            TrustServerCertificate: false,
            ConnectionTimeoutSeconds: 9);

        var connectionString = MsSqlAuthenticatedProbe.BuildConnectionString(target, authentication);
        var builder = new SqlConnectionStringBuilder(connectionString);

        Assert.Equal("demo-mssql-target,1433", builder.DataSource);
        Assert.Equal("DbScannerDemo", builder.InitialCatalog);
        Assert.Equal("dbscanner_scanner", builder.UserID);
        Assert.Equal(authentication.Password, builder.Password);
        Assert.True(builder.Encrypt == SqlConnectionEncryptOption.Mandatory);
        Assert.False(builder.TrustServerCertificate);
        Assert.False(builder.PersistSecurityInfo);
        Assert.False(builder.Pooling);
        Assert.False(builder.IntegratedSecurity);
        Assert.False(builder.MultipleActiveResultSets);
        Assert.Equal(9, builder.ConnectTimeout);
    }

    [Fact]
    public async Task Runtime_MarksAuthenticatedChecksSkippedWhenCredentialsAreAbsent()
    {
        var nmapRuntime = new NmapScannerRuntime(
            Options.Create(new ScannerOptions
            {
                ExecutablePath = "nmap-does-not-exist",
                TimeoutSeconds = 1,
                UseSampleOutputWhenUnavailable = true
            }),
            new StubMySqlEmptyPasswordProbe(),
            NullLogger<NmapScannerRuntime>.Instance);
        var runtime = new MsSqlScannerRuntime(
            new ScriptCompatibilityScannerRuntime(nmapRuntime),
            new FailingAuthenticatedProbe(),
            new PassingBrowserProbe());
        var target = new Target
        {
            Name = "Demo Microsoft SQL Server",
            Host = "demo-mssql-target",
            Port = 1433,
            Engine = DatabaseEngine.MsSql
        };
        var scan = new Scan { TargetId = target.Id, Target = target };
        var authenticatedScript = CreateScript(
            "mssql-xp-cmdshell",
            MsSqlProbeScripts.XpCmdShell,
            requiresCredentials: true);

        var result = await runtime.RunAsync(scan, target, [authenticatedScript]);

        var scriptResult = Assert.Single(result.ScriptResults);
        Assert.Equal(ProbeResultStatus.Skipped, scriptResult.ResultStatus);
        Assert.Contains("credentials_not_provided", scriptResult.NormalizedOutput);
    }

    [Fact]
    public async Task Runtime_PreservesDiscoveryWhenStoredCredentialsAreRejected()
    {
        var nmapRuntime = new NmapScannerRuntime(
            Options.Create(new ScannerOptions
            {
                ExecutablePath = "nmap-does-not-exist",
                TimeoutSeconds = 1,
                UseSampleOutputWhenUnavailable = true
            }),
            new StubMySqlEmptyPasswordProbe(),
            NullLogger<NmapScannerRuntime>.Instance);
        var runtime = new MsSqlScannerRuntime(
            new ScriptCompatibilityScannerRuntime(nmapRuntime),
            new AuthenticationFailedProbe(),
            new PassingBrowserProbe());
        var target = new Target
        {
            Name = "Demo Microsoft SQL Server",
            Host = "demo-mssql-target",
            Port = 1433,
            Engine = DatabaseEngine.MsSql
        };
        var scan = new Scan { TargetId = target.Id, Target = target };
        var discoveryScript = CreateScript("mssql-info", "ms-sql-info");
        var authenticatedScript = CreateScript(
            "mssql-version",
            MsSqlProbeScripts.Version,
            requiresCredentials: true);

        var result = await runtime.RunAsync(
            scan,
            target,
            [discoveryScript, authenticatedScript],
            new DatabaseAuthentication("DbScannerDemo", "scanner", "wrong-password"));

        Assert.Contains(
            result.ScriptResults,
            scriptResult => scriptResult.ScriptName == "ms-sql-info");
        Assert.Equal(
            ProbeResultStatus.AuthenticationFailed,
            Assert.Single(
                result.ScriptResults,
                scriptResult => scriptResult.ScriptName == MsSqlProbeScripts.Version)
                .ResultStatus);
    }

    private static AesGcmScanCredentialProtector CreateProtector()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        return new AesGcmScanCredentialProtector(
            Options.Create(new CredentialProtectionOptions { Key = key }));
    }

    private static NseScript CreateScript(
        string id,
        string name,
        bool requiresCredentials = false) =>
        new()
        {
            Id = id,
            NseScriptName = name,
            DisplayName = id,
            Description = id,
            SupportedEngines = "mssql",
            DefaultEnabled = true,
            ParserType = ParserType.GenericOutput,
            MatcherType = MatcherType.Contains,
            ApprovalStatus = ScriptApprovalStatus.Approved,
            RequiresCredentials = requiresCredentials
        };

    private sealed class FailingAuthenticatedProbe : IMsSqlAuthenticatedProbe
    {
        public Task<IReadOnlyList<ScanScriptExecutionResult>> RunAsync(
            Target target,
            IReadOnlyCollection<NseScript> scripts,
            DatabaseAuthentication authentication,
            CancellationToken cancellationToken = default) =>
            throw new Xunit.Sdk.XunitException("Authenticated probe must not run without credentials.");
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

    private sealed class AuthenticationFailedProbe : IMsSqlAuthenticatedProbe
    {
        public Task<IReadOnlyList<ScanScriptExecutionResult>> RunAsync(
            Target target,
            IReadOnlyCollection<NseScript> scripts,
            DatabaseAuthentication authentication,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScanScriptExecutionResult>>(
                scripts.Select(script => new ScanScriptExecutionResult(
                    script.Id,
                    script.NseScriptName,
                    null,
                    "status=authentication_failed; reason=authentication_failed",
                    1,
                    ProbeResultStatus.AuthenticationFailed)).ToArray());
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
}
