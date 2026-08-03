using DbScanner.Application.Models;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Scanning;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

public sealed class OracleSecurityTests
{
    [Fact]
    public void EngineMapping_UsesCanonicalApiValueAndDisplayName()
    {
        Assert.Equal("oracle", DatabaseEngine.Oracle.ToApiString());
        Assert.Equal("Oracle Database", DatabaseEngine.Oracle.ToDisplayName());
        Assert.True(DatabaseEngineExtensions.TryParseApiString("oracle", out var engine));
        Assert.Equal(DatabaseEngine.Oracle, engine);
    }

    [Fact]
    public void ConnectionStringBuilder_ProtectsDelimitersAndUsesSafeRuntimeSettings()
    {
        var target = new Target
        {
            Name = "Demo Oracle Database",
            Host = "demo-oracle-target",
            Port = 1521,
            Engine = DatabaseEngine.Oracle
        };
        var authentication = new DatabaseAuthentication(
            "FREEPDB1",
            "dbscanner_scanner",
            "P@ss;word=still-safe",
            ConnectionTimeoutSeconds: 9);

        var connectionString = OracleAuthenticatedProbe.BuildConnectionString(target, authentication);
        var builder = new OracleConnectionStringBuilder(connectionString);

        Assert.Equal("//demo-oracle-target:1521/FREEPDB1", builder.DataSource);
        Assert.Equal("dbscanner_scanner", builder.UserID);
        Assert.Equal(authentication.Password, builder.Password);
        Assert.False(builder.PersistSecurityInfo);
        Assert.False(builder.Pooling);
        Assert.Equal(9, builder.ConnectionTimeout);
    }

    [Fact]
    public async Task Runtime_MarksAuthenticatedChecksSkippedWhenCredentialsAreAbsent()
    {
        var runtime = CreateRuntime(new FailingOracleProbe());
        var target = CreateTarget();
        var scan = new Scan { TargetId = target.Id, Target = target };
        var discoveryScript = CreateScript(OracleProbeScripts.TnsVersion);
        var authenticatedScript = CreateScript(
            OracleProbeScripts.DbaUsers,
            requiresCredentials: true);

        var result = await runtime.RunAsync(
            scan,
            target,
            [discoveryScript, authenticatedScript]);

        Assert.Contains(
            result.ScriptResults,
            scriptResult => scriptResult.ScriptName == OracleProbeScripts.TnsVersion);
        var scriptResult = Assert.Single(
            result.ScriptResults,
            item => item.ScriptName == OracleProbeScripts.DbaUsers);
        Assert.Equal(ProbeResultStatus.Skipped, scriptResult.ResultStatus);
        Assert.Contains("credentials_not_provided", scriptResult.NormalizedOutput);
    }

    [Fact]
    public async Task Runtime_PreservesDiscoveryWhenStoredServiceNameIsInvalid()
    {
        var runtime = CreateRuntime(
            new OracleAuthenticatedProbe(NullLogger<OracleAuthenticatedProbe>.Instance));
        var target = CreateTarget();
        var scan = new Scan { TargetId = target.Id, Target = target };
        var discovery = CreateScript(OracleProbeScripts.TnsVersion);
        var authenticated = CreateScript(OracleProbeScripts.Version, requiresCredentials: true);

        var result = await runtime.RunAsync(
            scan,
            target,
            [discovery, authenticated],
            new DatabaseAuthentication("FREE PDB1", "scanner", "not-logged"));

        Assert.Contains(
            result.ScriptResults,
            scriptResult => scriptResult.ScriptName == OracleProbeScripts.TnsVersion);
        var authenticationResult = Assert.Single(
            result.ScriptResults,
            scriptResult => scriptResult.ScriptName == OracleProbeScripts.Version);
        Assert.Equal(ProbeResultStatus.ConnectionFailed, authenticationResult.ResultStatus);
        Assert.DoesNotContain("not-logged", authenticationResult.NormalizedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runtime_PreservesDiscoveryWhenAuthenticationFails()
    {
        var runtime = CreateRuntime(new AuthenticationFailedOracleProbe());
        var target = CreateTarget();
        var scan = new Scan { TargetId = target.Id, Target = target };
        var discovery = CreateScript(OracleProbeScripts.TnsVersion);
        var authenticated = CreateScript(OracleProbeScripts.Version, requiresCredentials: true);

        var result = await runtime.RunAsync(
            scan,
            target,
            [discovery, authenticated],
            new DatabaseAuthentication("FREEPDB1", "scanner", "wrong-password"));

        Assert.Contains(
            result.ScriptResults,
            scriptResult => scriptResult.ScriptName == OracleProbeScripts.TnsVersion);
        Assert.Equal(
            ProbeResultStatus.AuthenticationFailed,
            Assert.Single(
                result.ScriptResults,
                scriptResult => scriptResult.ScriptName == OracleProbeScripts.Version)
                .ResultStatus);
    }

    private static OracleScannerRuntime CreateRuntime(IOracleAuthenticatedProbe oracleProbe)
    {
        var nmap = new NmapScannerRuntime(
            Options.Create(new ScannerOptions
            {
                ExecutablePath = "nmap-does-not-exist",
                TimeoutSeconds = 1,
                UseSampleOutputWhenUnavailable = true
            }),
            new StubMySqlEmptyPasswordProbe(),
            NullLogger<NmapScannerRuntime>.Instance);
        var mssql = new MsSqlScannerRuntime(
            new ScriptCompatibilityScannerRuntime(nmap),
            new FailingMsSqlProbe(),
            new PassingBrowserProbe());
        return new OracleScannerRuntime(mssql, oracleProbe);
    }

    private static Target CreateTarget() =>
        new()
        {
            Name = "Demo Oracle Database",
            Host = "demo-oracle-target",
            Port = 1521,
            Engine = DatabaseEngine.Oracle
        };

    private static NseScript CreateScript(string name, bool requiresCredentials = false) =>
        new()
        {
            Id = name,
            NseScriptName = name,
            DisplayName = name,
            Description = name,
            SupportedEngines = "oracle",
            DefaultEnabled = true,
            ParserType = ParserType.GenericOutput,
            MatcherType = MatcherType.Contains,
            ApprovalStatus = ScriptApprovalStatus.Approved,
            RequiresCredentials = requiresCredentials
        };

    private sealed class FailingOracleProbe : IOracleAuthenticatedProbe
    {
        public Task<IReadOnlyList<ScanScriptExecutionResult>> RunAsync(
            Target target,
            IReadOnlyCollection<NseScript> scripts,
            DatabaseAuthentication authentication,
            CancellationToken cancellationToken = default) =>
            throw new Xunit.Sdk.XunitException("Oracle authenticated probe must not run.");
    }

    private sealed class AuthenticationFailedOracleProbe : IOracleAuthenticatedProbe
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

    private sealed class FailingMsSqlProbe : IMsSqlAuthenticatedProbe
    {
        public Task<IReadOnlyList<ScanScriptExecutionResult>> RunAsync(
            Target target,
            IReadOnlyCollection<NseScript> scripts,
            DatabaseAuthentication authentication,
            CancellationToken cancellationToken = default) =>
            throw new Xunit.Sdk.XunitException("Microsoft SQL Server probe was not expected.");
    }

    private sealed class PassingBrowserProbe : IMsSqlBrowserProbe
    {
        public Task<ScanScriptExecutionResult> RunAsync(
            Target target,
            NseScript script,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScanScriptExecutionResult(
                script.Id, script.NseScriptName, null, "status=passed", 0, ProbeResultStatus.Passed));
    }

    private sealed class StubMySqlEmptyPasswordProbe : IMySqlEmptyPasswordProbe
    {
        public Task<ScanScriptExecutionResult> RunAsync(
            Target target,
            NseScript script,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScanScriptExecutionResult(
                script.Id, script.NseScriptName, null, "status=skipped", 0, ProbeResultStatus.Skipped));
    }
}
