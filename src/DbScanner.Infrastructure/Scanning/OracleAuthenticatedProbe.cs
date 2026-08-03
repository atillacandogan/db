using System.Data;
using System.Globalization;
using System.Xml.Linq;
using DbScanner.Application.Models;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

namespace DbScanner.Infrastructure.Scanning;

internal sealed class OracleAuthenticatedProbe(
    ILogger<OracleAuthenticatedProbe> logger) : IOracleAuthenticatedProbe
{
    public async Task<IReadOnlyList<ScanScriptExecutionResult>> RunAsync(
        Target target,
        IReadOnlyCollection<NseScript> scripts,
        DatabaseAuthentication authentication,
        CancellationToken cancellationToken = default)
    {
        if (scripts.Count == 0)
        {
            return [];
        }

        await using var connection = new OracleConnection();
        try
        {
            connection.ConnectionString = BuildConnectionString(target, authentication);
            await connection.OpenAsync(cancellationToken);
        }
        catch (Exception exception) when (IsHandledProviderException(exception))
        {
            var (status, reason) = ClassifyConnectionFailure(exception);
            logger.LogWarning(
                "Authenticated Oracle Database connection failed for target {TargetId} with category {Category}.",
                target.Id,
                reason);
            return scripts
                .Select(script => CreateResult(script, status, reason, exitCode: 1))
                .ToArray();
        }

        var results = new List<ScanScriptExecutionResult>(scripts.Count);
        foreach (var script in scripts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await RunScriptAsync(
                connection,
                script,
                authentication.ConnectionTimeoutSeconds,
                cancellationToken));
        }

        return results;
    }

    internal static string BuildConnectionString(
        Target target,
        DatabaseAuthentication authentication)
    {
        var host = target.Host.Trim();
        if (host.Length == 0 || host.Any(character =>
                char.IsWhiteSpace(character)
                || character is '/' or '\\' or '(' or ')' or ';' or '='))
        {
            throw new InvalidOperationException("Oracle target host contains unsupported characters.");
        }

        var serviceName = authentication.Database.Trim();
        if (serviceName.Any(character =>
                char.IsWhiteSpace(character)
                || character is '/' or '\\' or '(' or ')' or ';'))
        {
            throw new InvalidOperationException("Oracle service name contains unsupported characters.");
        }

        return new OracleConnectionStringBuilder
        {
            DataSource = $"//{host}:{target.Port}/{serviceName}",
            UserID = authentication.Username.Trim(),
            Password = authentication.Password,
            Pooling = false,
            PersistSecurityInfo = false,
            ConnectionTimeout = Math.Clamp(authentication.ConnectionTimeoutSeconds, 1, 30)
        }.ConnectionString;
    }

    private async Task<ScanScriptExecutionResult> RunScriptAsync(
        OracleConnection connection,
        NseScript script,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            return script.NseScriptName.ToLowerInvariant() switch
            {
                OracleProbeScripts.Version => await RunVersionAsync(
                    connection, script, commandTimeoutSeconds, cancellationToken),
                OracleProbeScripts.SessionPrivileges => await RunInventoryAsync(
                    connection,
                    script,
                    """
                    SELECT
                      (SELECT COUNT(*) FROM SESSION_ROLES) || ',' ||
                      (SELECT COUNT(*) FROM SESSION_PRIVS)
                    FROM DUAL
                    """,
                    "roles_and_privileges",
                    commandTimeoutSeconds,
                    cancellationToken),
                OracleProbeScripts.DatabaseMetadata => await RunInventoryAsync(
                    connection,
                    script,
                    """
                    SELECT NAME || ',' || PLATFORM_NAME || ',' || LOG_MODE || ',' || OPEN_MODE
                    FROM V$DATABASE
                    """,
                    "database_metadata",
                    commandTimeoutSeconds,
                    cancellationToken),
                OracleProbeScripts.DbaUsers => await RunCountRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT COUNT(*)
                    FROM DBA_ROLE_PRIVS
                    WHERE GRANTED_ROLE = 'DBA'
                      AND GRANTEE NOT IN ('SYS', 'SYSTEM')
                    """,
                    "non_system_dba_users",
                    commandTimeoutSeconds,
                    cancellationToken),
                OracleProbeScripts.RiskyUsers => await RunCountRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT COUNT(*)
                    FROM DBA_USERS
                    WHERE ACCOUNT_STATUS LIKE 'OPEN%'
                      AND USERNAME IN (
                        'SCOTT', 'HR', 'OE', 'SH', 'PM', 'IX', 'DBSNMP',
                        'OUTLN', 'MDSYS', 'CTXSYS', 'XDB')
                    """,
                    "open_default_or_risky_users",
                    commandTimeoutSeconds,
                    cancellationToken),
                OracleProbeScripts.UnlockedUnusedAccounts => await RunCountRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT COUNT(*)
                    FROM DBA_USERS
                    WHERE ACCOUNT_STATUS LIKE 'OPEN%'
                      AND ORACLE_MAINTAINED = 'Y'
                      AND USERNAME NOT IN ('SYS', 'SYSTEM')
                      AND (LAST_LOGIN IS NULL OR LAST_LOGIN < SYSTIMESTAMP - INTERVAL '90' DAY)
                    """,
                    "open_unused_oracle_accounts",
                    commandTimeoutSeconds,
                    cancellationToken),
                OracleProbeScripts.PasswordProfiles => await RunCountRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT COUNT(*)
                    FROM DBA_PROFILES
                    WHERE RESOURCE_TYPE = 'PASSWORD'
                      AND (
                        (RESOURCE_NAME = 'FAILED_LOGIN_ATTEMPTS' AND LIMIT IN ('UNLIMITED', 'DEFAULT'))
                        OR (RESOURCE_NAME = 'PASSWORD_LIFE_TIME' AND LIMIT = 'UNLIMITED')
                        OR (RESOURCE_NAME = 'PASSWORD_VERIFY_FUNCTION' AND LIMIT IN ('NULL', 'DEFAULT'))
                      )
                    """,
                    "weak_password_profile_settings",
                    commandTimeoutSeconds,
                    cancellationToken),
                OracleProbeScripts.AuditConfiguration => await RunInverseCountRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT COUNT(*)
                    FROM AUDIT_UNIFIED_ENABLED_POLICIES
                    WHERE ENABLED_OPTION <> 'NONE'
                    """,
                    "enabled_audit_policies",
                    commandTimeoutSeconds,
                    cancellationToken),
                OracleProbeScripts.RiskySystemPrivileges => await RunCountRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT COUNT(*)
                    FROM DBA_SYS_PRIVS
                    WHERE PRIVILEGE IN (
                      'ALTER SYSTEM', 'CREATE ANY PROCEDURE', 'EXECUTE ANY PROCEDURE',
                      'CREATE ANY JOB', 'BECOME USER', 'GRANT ANY PRIVILEGE',
                      'GRANT ANY ROLE', 'SELECT ANY DICTIONARY')
                      AND GRANTEE NOT IN ('SYS', 'SYSTEM', 'DBA')
                    """,
                    "risky_system_privilege_grants",
                    commandTimeoutSeconds,
                    cancellationToken),
                OracleProbeScripts.PublicPrivileges => await RunCountRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT
                      (SELECT COUNT(*) FROM DBA_SYS_PRIVS
                       WHERE GRANTEE = 'PUBLIC'
                         AND PRIVILEGE IN ('CREATE DATABASE LINK', 'EXECUTE ANY PROCEDURE'))
                      +
                      (SELECT COUNT(*) FROM DBA_TAB_PRIVS
                       WHERE GRANTEE = 'PUBLIC'
                         AND PRIVILEGE = 'EXECUTE'
                         AND TABLE_NAME IN (
                           'UTL_HTTP', 'UTL_FILE', 'UTL_TCP', 'DBMS_SCHEDULER',
                           'DBMS_JAVA', 'DBMS_XMLQUERY'))
                    FROM DUAL
                    """,
                    "risky_public_privilege_grants",
                    commandTimeoutSeconds,
                    cancellationToken),
                _ => CreateResult(
                    script,
                    ProbeResultStatus.NotSupported,
                    "check_not_supported",
                    exitCode: 0)
            };
        }
        catch (Exception exception) when (IsHandledProviderException(exception))
        {
            var (status, reason) = ClassifyQueryFailure(exception);
            logger.LogWarning(
                "Authenticated Oracle Database check {Check} failed for target database with category {Category}.",
                script.NseScriptName,
                reason);
            return CreateResult(script, status, reason, exitCode: 1);
        }
    }

    private static async Task<ScanScriptExecutionResult> RunVersionAsync(
        OracleConnection connection,
        NseScript script,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            """
            SELECT BANNER
            FROM V$VERSION
            WHERE UPPER(BANNER) LIKE '%ORACLE%'
              AND UPPER(BANNER) LIKE '%DATABASE%'
            FETCH FIRST 1 ROW ONLY
            """,
            commandTimeoutSeconds);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        var banner = Convert.ToString(value, CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(banner))
        {
            return CreateResult(script, ProbeResultStatus.NotSupported, "version_not_available", 0);
        }

        return CreateResult(
            script,
            ProbeResultStatus.Passed,
            $"product=Oracle Database,version_and_edition={Normalize(banner)}",
            0);
    }

    private static async Task<ScanScriptExecutionResult> RunInventoryAsync(
        OracleConnection connection,
        NseScript script,
        string query,
        string evidenceKey,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, query, commandTimeoutSeconds);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return CreateResult(
            script,
            ProbeResultStatus.Passed,
            $"{evidenceKey}={Normalize(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "unavailable")}",
            0);
    }

    private static async Task<ScanScriptExecutionResult> RunCountRiskCheckAsync(
        OracleConnection connection,
        NseScript script,
        string query,
        string evidenceKey,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var count = await ExecuteInt64Async(connection, query, commandTimeoutSeconds, cancellationToken);
        return CreateResult(
            script,
            count > 0 ? ProbeResultStatus.Failed : ProbeResultStatus.Passed,
            $"{evidenceKey}={count}",
            0);
    }

    private static async Task<ScanScriptExecutionResult> RunInverseCountRiskCheckAsync(
        OracleConnection connection,
        NseScript script,
        string query,
        string evidenceKey,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var count = await ExecuteInt64Async(connection, query, commandTimeoutSeconds, cancellationToken);
        return CreateResult(
            script,
            count == 0 ? ProbeResultStatus.Failed : ProbeResultStatus.Passed,
            $"{evidenceKey}={count}",
            0);
    }

    private static async Task<long> ExecuteInt64Async(
        OracleConnection connection,
        string query,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, query, commandTimeoutSeconds);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull
            ? 0
            : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static OracleCommand CreateCommand(
        OracleConnection connection,
        string query,
        int commandTimeoutSeconds) =>
        new(query, connection)
        {
            CommandType = CommandType.Text,
            CommandTimeout = Math.Clamp(commandTimeoutSeconds, 1, 30),
            BindByName = true
        };

    private static ScanScriptExecutionResult CreateResult(
        NseScript script,
        ProbeResultStatus status,
        string evidence,
        int exitCode)
    {
        var checkName = script.NseScriptName.StartsWith("oracle-auth-", StringComparison.OrdinalIgnoreCase)
            ? script.NseScriptName["oracle-auth-".Length..]
            : script.NseScriptName;
        var output = string.Join(
            "; ",
            $"check={Normalize(checkName)}",
            $"status={ToToken(status)}",
            $"evidence={Normalize(evidence)}");
        var rawXml = new XElement(
            "script",
            new XAttribute("id", script.NseScriptName),
            new XAttribute("runner", "dbscanner-oracle-authenticated"),
            new XAttribute("output", output))
            .ToString(SaveOptions.DisableFormatting);
        return new ScanScriptExecutionResult(
            script.Id,
            script.NseScriptName,
            rawXml,
            output,
            exitCode,
            status);
    }

    private static bool IsHandledProviderException(Exception exception) =>
        exception is OracleException
            or TimeoutException
            or InvalidOperationException;

    private static (ProbeResultStatus Status, string Reason) ClassifyConnectionFailure(Exception exception)
    {
        if (exception is TimeoutException)
        {
            return (ProbeResultStatus.ConnectionFailed, "connection_timeout");
        }

        if (exception is not OracleException oracleException)
        {
            return (ProbeResultStatus.ConnectionFailed, "unexpected_provider_error");
        }

        return oracleException.Number switch
        {
            1017 or 28000 or 28001 => (ProbeResultStatus.AuthenticationFailed, "authentication_failed"),
            12154 => (ProbeResultStatus.ConnectionFailed, "service_name_not_resolved"),
            12505 or 12514 => (ProbeResultStatus.ConnectionFailed, "service_unavailable"),
            12541 or 12543 or 12545 => (ProbeResultStatus.ConnectionFailed, "host_unreachable"),
            12535 or 12570 => (ProbeResultStatus.ConnectionFailed, "connection_timeout"),
            _ => (ProbeResultStatus.ConnectionFailed, "unexpected_provider_error")
        };
    }

    private static (ProbeResultStatus Status, string Reason) ClassifyQueryFailure(Exception exception)
    {
        if (exception is OracleException { Number: 1031 or 942 })
        {
            return (ProbeResultStatus.InsufficientPrivileges, "insufficient_privileges");
        }

        if (exception is OracleException { Number: 1013 } or TimeoutException)
        {
            return (ProbeResultStatus.ConnectionFailed, "query_timeout");
        }

        return (ProbeResultStatus.ConnectionFailed, "unexpected_provider_error");
    }

    private static string ToToken(ProbeResultStatus status) => status switch
    {
        ProbeResultStatus.NotSupported => "not_supported",
        ProbeResultStatus.InsufficientPrivileges => "insufficient_privileges",
        ProbeResultStatus.ConnectionFailed => "connection_failed",
        ProbeResultStatus.AuthenticationFailed => "authentication_failed",
        _ => status.ToString().ToLowerInvariant()
    };

    private static string Normalize(string value) =>
        value
            .Replace(';', '_')
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
}
