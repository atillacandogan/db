using DbScanner.Application.Models;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Scanning;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Logging.Abstractions;
using Oracle.ManagedDataAccess.Client;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

public sealed class OracleContainerIntegrationTests
{
    private const string Image = "gvenzl/oracle-free:23.26.1-slim";

    [DockerFact]
    public async Task AuthenticatedProbe_ExercisesRealOracleDatabaseSafely()
    {
        var systemPassword = CreatePassword();
        var scannerPassword = CreatePassword();
        var initDirectory = Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "docker",
                "oracle-init"));
        await using var container = new ContainerBuilder(Image)
            .WithEnvironment("ORACLE_PASSWORD", systemPassword)
            .WithEnvironment("APP_USER", "dbscanner_scanner")
            .WithEnvironment("APP_USER_PASSWORD", scannerPassword)
            .WithBindMount(
                initDirectory,
                "/container-entrypoint-initdb.d",
                AccessMode.ReadOnly)
            .WithPortBinding(1521, true)
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilMessageIsLogged("DATABASE IS READY TO USE!"))
            .Build();

        await container.StartAsync();
        var target = new Target
        {
            Name = "Testcontainers Oracle Database",
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(1521),
            Engine = DatabaseEngine.Oracle
        };
        var authentication = new DatabaseAuthentication(
            "FREEPDB1",
            "dbscanner_scanner",
            scannerPassword,
            ConnectionTimeoutSeconds: 15);
        var probe = new OracleAuthenticatedProbe(
            NullLogger<OracleAuthenticatedProbe>.Instance);

        var results = await probe.RunAsync(
            target,
            [
                CreateScript(OracleProbeScripts.Version),
                CreateScript(OracleProbeScripts.SessionPrivileges),
                CreateScript(OracleProbeScripts.DbaUsers),
                CreateScript(OracleProbeScripts.RiskyUsers),
                CreateScript(OracleProbeScripts.UnlockedUnusedAccounts),
                CreateScript(OracleProbeScripts.PasswordProfiles),
                CreateScript(OracleProbeScripts.AuditConfiguration),
                CreateScript(OracleProbeScripts.RiskySystemPrivileges),
                CreateScript(OracleProbeScripts.PublicPrivileges),
                CreateScript(OracleProbeScripts.DatabaseMetadata)
            ],
            authentication);

        Assert.Equal(
            ProbeResultStatus.Passed,
            ResultFor(results, OracleProbeScripts.Version).ResultStatus);
        Assert.Contains(
            "Oracle Database",
            ResultFor(results, OracleProbeScripts.Version).NormalizedOutput);
        Assert.Equal(
            ProbeResultStatus.Passed,
            ResultFor(results, OracleProbeScripts.SessionPrivileges).ResultStatus);
        Assert.Equal(10, results.Count);
        Assert.All(
            results,
            result => Assert.True(
                result.ResultStatus is ProbeResultStatus.Passed or ProbeResultStatus.Failed,
                $"{result.ScriptName}: {result.NormalizedOutput}"));
        Assert.DoesNotContain(
            scannerPassword,
            string.Join(Environment.NewLine, results.Select(result => result.NormalizedOutput)),
            StringComparison.Ordinal);

        var rejected = await probe.RunAsync(
            target,
            [CreateScript(OracleProbeScripts.Version)],
            authentication with { Password = "definitely-wrong" });
        Assert.Equal(
            ProbeResultStatus.AuthenticationFailed,
            Assert.Single(rejected).ResultStatus);

        var connectionString = OracleAuthenticatedProbe.BuildConnectionString(target, authentication);
        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SAMPLE_ASSETS";
        Assert.Equal(2m, await command.ExecuteScalarAsync());
    }

    private static string CreatePassword() => $"DbScanner_{Guid.NewGuid():N}_Aa1";

    private static NseScript CreateScript(string name) =>
        new()
        {
            Id = $"integration-{name}",
            NseScriptName = name,
            DisplayName = name,
            Description = name,
            SupportedEngines = "oracle",
            DefaultEnabled = true,
            RequiresCredentials = true,
            ParserType = ParserType.GenericOutput,
            MatcherType = MatcherType.Contains,
            ApprovalStatus = ScriptApprovalStatus.Approved
        };

    private static ScanScriptExecutionResult ResultFor(
        IReadOnlyList<ScanScriptExecutionResult> results,
        string scriptName) =>
        Assert.Single(results, result => result.ScriptName == scriptName);
}
