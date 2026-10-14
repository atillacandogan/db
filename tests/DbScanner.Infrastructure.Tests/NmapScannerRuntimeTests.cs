using DbScanner.Application.Models;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Scanning;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

public sealed class NmapScannerRuntimeTests
{
    [Fact]
    public async Task RunAsync_UsesSampleOutputWhenExecutableIsUnavailable()
    {
        var runtime = new NmapScannerRuntime(
            Options.Create(new ScannerOptions
            {
                ExecutablePath = "nmap-does-not-exist",
                TimeoutSeconds = 1,
                UseSampleOutputWhenUnavailable = true
            }),
            new StubMySqlEmptyPasswordProbe(),
            NullLogger<NmapScannerRuntime>.Instance);

        var target = new Target
        {
            Name = "Demo PostgreSQL",
            Host = "demo-postgres-target",
            Port = 5432,
            Engine = DatabaseEngine.PostgreSql
        };

        var scan = new Scan
        {
            TargetId = target.Id,
            Target = target
        };

        var scripts = new[]
        {
            new NseScript
            {
                Id = "banner",
                NseScriptName = "banner",
                DisplayName = "Banner",
                Description = "Banner",
                SupportedEngines = "postgresql",
                DefaultEnabled = true,
                ParserType = ParserType.GenericOutput,
                MatcherType = MatcherType.Contains,
                ApprovalStatus = ScriptApprovalStatus.Approved
            }
        };

        var result = await runtime.RunAsync(scan, target, scripts);

        Assert.Contains("postgresql", result.StructuredOutput, StringComparison.OrdinalIgnoreCase);
        var scriptResult = Assert.Single(result.ScriptResults);
        Assert.Equal("banner", scriptResult.ScriptName);
        Assert.Contains("PostgreSQL server ready", scriptResult.NormalizedOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("mysql_native_password", true)]
    [InlineData("MYSQL_NATIVE_PASSWORD", true)]
    [InlineData("caching_sha2_password", false)]
    [InlineData(null, false)]
    public void ShouldUseLegacyMySqlEmptyPasswordScript_RoutesOnlyNativePassword(
        string? authPlugin,
        bool expected)
    {
        Assert.Equal(
            expected,
            NmapScannerRuntime.ShouldUseLegacyMySqlEmptyPasswordScript(authPlugin));
    }

    [Fact]
    public void ParseMySqlAuthPlugin_ReadsPluginFromMysqlInfoXml()
    {
        const string xml =
            """
            <nmaprun>
              <host>
                <ports>
                  <port>
                    <script id="mysql-info">
                      <table>
                        <elem key="Protocol">10</elem>
                        <elem key="Auth Plugin Name">caching_sha2_password</elem>
                      </table>
                    </script>
                  </port>
                </ports>
              </host>
            </nmaprun>
            """;

        Assert.Equal(
            "caching_sha2_password",
            NmapScannerRuntime.ParseMySqlAuthPlugin(xml));
    }

    [Fact]
    public async Task CompatibilityRuntime_SkipsScriptsFromOtherEnginesForMsSqlTarget()
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
        var runtime = new ScriptCompatibilityScannerRuntime(nmapRuntime);
        var target = new Target
        {
            Name = "Demo MSSQL",
            Host = "demo-mssql-target",
            Port = 1433,
            Engine = DatabaseEngine.MsSql
        };
        var scan = new Scan { TargetId = target.Id, Target = target };
        var scripts = new[]
        {
            CreateScript("ms-sql-info", "mssql"),
            CreateScript("mysql-info", "mysql"),
            CreateScript("pgsql-info", "postgresql")
        };

        var result = await runtime.RunAsync(scan, target, scripts);

        var executed = Assert.Single(result.ScriptResults, item => item.ScriptName == "ms-sql-info");
        Assert.Contains("Microsoft SQL Server", executed.NormalizedOutput);

        var skipped = Assert.Single(result.ScriptResults, item => item.ScriptName == "mysql-info");
        Assert.Contains("status=skipped", skipped.NormalizedOutput);
        Assert.Contains("not compatible with Microsoft SQL Server target", skipped.NormalizedOutput);

        var skippedPostgreSql = Assert.Single(result.ScriptResults, item => item.ScriptName == "pgsql-info");
        Assert.Contains("status=skipped", skippedPostgreSql.NormalizedOutput);
        Assert.Contains("not compatible with Microsoft SQL Server target", skippedPostgreSql.NormalizedOutput);
    }

    [Fact]
    public void MsSqlRuntime_UsesServiceFingerprintWhenNseScriptReturnsAnError()
    {
        var script = CreateScript("ms-sql-info", "mssql");
        const string xml =
            """
            <nmaprun>
              <host>
                <ports>
                  <port protocol="tcp" portid="1433">
                    <state state="open" />
                    <service name="ms-sql-s" product="Microsoft SQL Server 2022" version="16.00.4265.00" />
                    <script id="ms-sql-info" output="ERROR: Script execution failed (use -d to debug)" />
                  </port>
                </ports>
              </host>
            </nmaprun>
            """;
        var result = new ScanExecutionResult(
            xml,
            "state=open; service=ms-sql-s; product=Microsoft SQL Server 2022",
            [
                new ScanScriptExecutionResult(
                    script.Id,
                    script.NseScriptName,
                    null,
                    "ERROR: Script execution failed (use -d to debug)",
                    0)
            ]);

        var normalized = MsSqlNmapResultNormalizer.NormalizeMsSqlInfoResult(result, [script]);

        var scriptResult = Assert.Single(normalized.ScriptResults);
        Assert.Contains("product=Microsoft SQL Server 2022", scriptResult.NormalizedOutput);
        Assert.Contains("version=16.00.4265.00", scriptResult.NormalizedOutput);
        Assert.DoesNotContain("ERROR:", scriptResult.NormalizedOutput);
        Assert.Contains("source=nmap-service-detection", normalized.StructuredOutput);
        Assert.DoesNotContain("ERROR:", normalized.StructuredOutput);
    }

    private static NseScript CreateScript(string id, string supportedEngines) =>
        new()
        {
            Id = id,
            NseScriptName = id,
            DisplayName = id,
            Description = id,
            SupportedEngines = supportedEngines,
            ParserType = ParserType.GenericOutput,
            MatcherType = MatcherType.Contains,
            ApprovalStatus = ScriptApprovalStatus.Approved
        };

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
                "status=not_vulnerable",
                0));
    }
}
