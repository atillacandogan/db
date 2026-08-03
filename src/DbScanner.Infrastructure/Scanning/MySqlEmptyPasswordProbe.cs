using System.Xml.Linq;
using DbScanner.Application.Models;
using DbScanner.Domain.Entities;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace DbScanner.Infrastructure.Scanning;

internal sealed class MySqlEmptyPasswordProbe(
    ILogger<MySqlEmptyPasswordProbe> logger) : IMySqlEmptyPasswordProbe
{
    private const int AccessDeniedErrorNumber = 1045;
    private const uint ConnectionTimeoutSeconds = 5;

    public async Task<ScanScriptExecutionResult> RunAsync(
        Target target,
        NseScript script,
        CancellationToken cancellationToken = default)
    {
        // An unknown user is intentional: MySQL may map it to an anonymous account.
        // No user is created and the target database is never modified.
        var anonymousProbeUser = $"dbscanner_probe_{Guid.NewGuid():N}";

        try
        {
            var anonymousResult = await TryEmptyPasswordAsync(
                target,
                anonymousProbeUser,
                cancellationToken);

            if (anonymousResult is not null)
            {
                return CreateCompletedResult(script, anonymousResult);
            }

            var rootResult = await TryEmptyPasswordAsync(target, "root", cancellationToken);
            return CreateCompletedResult(script, rootResult);
        }
        catch (Exception exception) when (exception is MySqlException or TimeoutException or InvalidOperationException)
        {
            logger.LogWarning(
                exception,
                "MySQL empty-password compatibility probe failed for target {TargetId}.",
                target.Id);

            var output = JoinOutput(
                ("check", "mysql_empty_password"),
                ("status", "error"),
                ("probe", "mysql84_compatible"),
                ("reason", GetErrorReason(exception)));

            return CreateResult(script, output, exitCode: 1);
        }
    }

    private static async Task<SuccessfulLogin?> TryEmptyPasswordAsync(
        Target target,
        string userName,
        CancellationToken cancellationToken)
    {
        var connectionString = new MySqlConnectionStringBuilder
        {
            Server = target.Host,
            Port = checked((uint)target.Port),
            UserID = userName,
            Password = string.Empty,
            ConnectionTimeout = ConnectionTimeoutSeconds,
            DefaultCommandTimeout = ConnectionTimeoutSeconds,
            Pooling = false,
            SslMode = MySqlSslMode.Preferred,
            AllowPublicKeyRetrieval = true
        }.ConnectionString;

        try
        {
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT CURRENT_USER(), VERSION()";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "MySQL authentication succeeded but identity could not be read.");
            }

            return new SuccessfulLogin(
                reader.GetString(0),
                reader.GetString(1));
        }
        catch (MySqlException exception) when (exception.Number == AccessDeniedErrorNumber)
        {
            return null;
        }
    }

    private static ScanScriptExecutionResult CreateCompletedResult(
        NseScript script,
        SuccessfulLogin? login)
    {
        if (login is null)
        {
            var safeOutput = JoinOutput(
                ("check", "mysql_empty_password"),
                ("status", "not_vulnerable"),
                ("probe", "mysql84_compatible"),
                ("evidence", "empty_password_login_rejected"));

            return CreateResult(script, safeOutput, exitCode: 0);
        }

        var accountType = login.AuthenticatedAccount.StartsWith('@')
            ? "anonymous"
            : login.AuthenticatedAccount.StartsWith("root@", StringComparison.OrdinalIgnoreCase)
                ? "root"
                : "named";

        var vulnerableOutput = JoinOutput(
            ("check", "mysql_empty_password"),
            ("status", "vulnerable"),
            ("probe", "mysql84_compatible"),
            ("account_type", accountType),
            ("authenticated_as", login.AuthenticatedAccount),
            ("server_version", login.ServerVersion),
            ("evidence", "empty_password_login_succeeded"));

        return CreateResult(script, vulnerableOutput, exitCode: 0);
    }

    private static ScanScriptExecutionResult CreateResult(
        NseScript script,
        string output,
        int exitCode)
    {
        var rawXml = new XElement(
            "script",
            new XAttribute("id", script.NseScriptName),
            new XAttribute("runner", "dbscanner-mysql84-compatible"),
            new XAttribute("output", output))
            .ToString(SaveOptions.DisableFormatting);

        return new ScanScriptExecutionResult(
            script.Id,
            script.NseScriptName,
            rawXml,
            output,
            exitCode);
    }

    private static string JoinOutput(params (string Key, string Value)[] values) =>
        string.Join("; ", values.Select(value => $"{value.Key}={NormalizeValue(value.Value)}"));

    private static string NormalizeValue(string value) =>
        value
            .Replace(';', '_')
            .Replace('=', '_')
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

    private static string GetErrorReason(Exception exception) => exception switch
    {
        TimeoutException => "timeout",
        InvalidOperationException => "invalid_probe_response",
        _ => "mysql_connection_error"
    };

    private sealed record SuccessfulLogin(
        string AuthenticatedAccount,
        string ServerVersion);
}
