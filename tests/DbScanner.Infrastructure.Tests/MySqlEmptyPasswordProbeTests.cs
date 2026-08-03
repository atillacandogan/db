using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Scanning;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

public sealed class MySqlEmptyPasswordProbeTests
{
    [Fact]
    public async Task RunAsync_DetectsEmptyPasswordOnConfiguredMySql84Target()
    {
        var host = Environment.GetEnvironmentVariable("DBSCANNER_TEST_MYSQL_HOST");
        var portValue = Environment.GetEnvironmentVariable("DBSCANNER_TEST_MYSQL_PORT");

        // This test stays dependency-free in normal CI and becomes an integration
        // test when an authorized disposable MySQL target is explicitly supplied.
        if (string.IsNullOrWhiteSpace(host) || !int.TryParse(portValue, out var port))
        {
            return;
        }

        var target = new Target
        {
            Name = "MySQL compatibility probe target",
            Host = host,
            Port = port,
            Engine = DatabaseEngine.MySql
        };
        var script = new NseScript
        {
            Id = "mysql-empty-password",
            NseScriptName = "mysql-empty-password",
            DisplayName = "MySQL Empty Password",
            Description = "Checks empty-password authentication.",
            SupportedEngines = "mysql",
            ParserType = ParserType.GenericOutput,
            MatcherType = MatcherType.KeyValue,
            ApprovalStatus = ScriptApprovalStatus.Approved
        };
        var probe = new MySqlEmptyPasswordProbe(
            NullLogger<MySqlEmptyPasswordProbe>.Instance);

        var result = await probe.RunAsync(target, script);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("probe=mysql84_compatible", result.NormalizedOutput);
        Assert.Contains("status=vulnerable", result.NormalizedOutput);
        Assert.Contains("authenticated_as=@%", result.NormalizedOutput);
    }
}
