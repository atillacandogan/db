using System.Data;
using System.Globalization;
using System.Xml.Linq;
using DbScanner.Application.Models;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DbScanner.Infrastructure.Scanning;

internal sealed class MsSqlAuthenticatedProbe(
    ILogger<MsSqlAuthenticatedProbe> logger) : IMsSqlAuthenticatedProbe
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

        await using var connection = new SqlConnection(BuildConnectionString(target, authentication));
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsHandledProviderException(exception))
        {
            var (status, reason) = ClassifyConnectionFailure(exception);
            logger.LogWarning(
                "Authenticated Microsoft SQL Server connection failed for target {TargetId} with category {Category}.",
                target.Id,
                reason);

            return scripts
                .Select(script => CreateResult(
                    script,
                    status,
                    reason,
                    exitCode: 1))
                .ToArray();
        }

        var results = new List<ScanScriptExecutionResult>(scripts.Count);
        foreach (var script in scripts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await RunCheckAsync(
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
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = $"{target.Host},{target.Port}",
            InitialCatalog = authentication.Database.Trim(),
            UserID = authentication.Username.Trim(),
            Password = authentication.Password,
            IntegratedSecurity = false,
            PersistSecurityInfo = false,
            Pooling = false,
            TrustServerCertificate = authentication.TrustServerCertificate,
            ConnectTimeout = authentication.ConnectionTimeoutSeconds,
            ApplicationName = "DbScanner.AuthenticatedProbe",
            MultipleActiveResultSets = false
        };
        builder["Encrypt"] = authentication.Encrypt;

        return builder.ConnectionString;
    }

    private async Task<ScanScriptExecutionResult> RunCheckAsync(
        SqlConnection connection,
        NseScript script,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            return script.NseScriptName switch
            {
                MsSqlProbeScripts.Version => await RunVersionAsync(
                    connection,
                    script,
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.CurrentLoginSysadmin => await RunBooleanRiskCheckAsync(
                    connection,
                    script,
                    "SELECT COALESCE(IS_SRVROLEMEMBER(N'sysadmin'), 0);",
                    "current_login_is_sysadmin",
                    "current_login_is_not_sysadmin",
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.LoginUserMapping => await RunCountRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT COUNT_BIG(*)
                    FROM sys.database_principals AS database_user
                    LEFT JOIN sys.server_principals AS server_login
                      ON database_user.sid = server_login.sid
                    WHERE database_user.authentication_type = 1
                      AND database_user.principal_id > 4
                      AND server_login.sid IS NULL;
                    """,
                    "orphaned_database_users",
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.ExcessiveSysadmin => await RunThresholdRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT COUNT_BIG(*)
                    FROM sys.server_role_members AS membership
                    INNER JOIN sys.server_principals AS role_principal
                      ON role_principal.principal_id = membership.role_principal_id
                    INNER JOIN sys.server_principals AS member_principal
                      ON member_principal.principal_id = membership.member_principal_id
                    WHERE role_principal.name = N'sysadmin'
                      AND member_principal.is_disabled = 0;
                    """,
                    threshold: 1,
                    evidenceKey: "enabled_sysadmin_members",
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.GuestAccess => await RunBooleanRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT CASE WHEN EXISTS (
                        SELECT 1
                        FROM sys.database_permissions AS permission
                        INNER JOIN sys.database_principals AS principal
                          ON principal.principal_id = permission.grantee_principal_id
                        WHERE principal.name = N'guest'
                          AND permission.permission_name = N'CONNECT'
                          AND permission.state IN (N'G', N'W')
                    ) THEN 1 ELSE 0 END;
                    """,
                    "guest_connect_granted",
                    "guest_connect_not_granted",
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.Trustworthy => await RunBooleanRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT CONVERT(int, is_trustworthy_on)
                    FROM sys.databases
                    WHERE database_id = DB_ID();
                    """,
                    "database_trustworthy_enabled",
                    "database_trustworthy_disabled",
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.XpCmdShell => await RunConfigurationCheckAsync(
                    connection,
                    script,
                    "xp_cmdshell",
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.OleAutomation => await RunConfigurationCheckAsync(
                    connection,
                    script,
                    "Ole Automation Procedures",
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.ClrEnabled => await RunConfigurationCheckAsync(
                    connection,
                    script,
                    "clr enabled",
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.AdHocDistributedQueries => await RunConfigurationCheckAsync(
                    connection,
                    script,
                    "Ad Hoc Distributed Queries",
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.CrossDatabaseOwnershipChaining => await RunBooleanRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT CASE WHEN
                        EXISTS (
                            SELECT 1
                            FROM sys.configurations
                            WHERE name = N'cross db ownership chaining'
                              AND value_in_use = 1)
                        OR EXISTS (
                            SELECT 1
                            FROM sys.databases
                            WHERE database_id > 4
                              AND is_db_chaining_on = 1)
                    THEN 1 ELSE 0 END;
                    """,
                    "cross_database_ownership_chaining_enabled",
                    "cross_database_ownership_chaining_disabled",
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.MixedMode => await RunBooleanRiskCheckAsync(
                    connection,
                    script,
                    "SELECT CASE WHEN SERVERPROPERTY(N'IsIntegratedSecurityOnly') = 0 THEN 1 ELSE 0 END;",
                    "mixed_mode_authentication_enabled",
                    "windows_authentication_only",
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.RiskyLogins => await RunCountRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT COUNT_BIG(*)
                    FROM sys.server_principals
                    WHERE is_disabled = 0
                      AND type = N'S'
                      AND LOWER(name) IN (N'sa', N'admin', N'administrator', N'test');
                    """,
                    "enabled_risky_named_logins",
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.Audit => await RunInverseCountRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT COUNT_BIG(*)
                    FROM sys.server_audits
                    WHERE is_state_enabled = 1;
                    """,
                    "enabled_server_audits",
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.PasswordPolicy => await RunCountRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT COUNT_BIG(*)
                    FROM sys.sql_logins
                    WHERE is_disabled = 0
                      AND is_policy_checked = 0;
                    """,
                    "logins_without_password_policy",
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.PasswordExpiration => await RunCountRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT COUNT_BIG(*)
                    FROM sys.sql_logins
                    WHERE is_disabled = 0
                      AND name <> N'sa'
                      AND is_expiration_checked = 0;
                    """,
                    "logins_without_password_expiration",
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.PublicServerPermissions => await RunCountRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT COUNT_BIG(*)
                    FROM sys.server_permissions AS permission
                    INNER JOIN sys.server_principals AS principal
                      ON principal.principal_id = permission.grantee_principal_id
                    WHERE principal.name = N'public'
                      AND permission.state IN (N'G', N'W')
                      AND permission.permission_name IN (
                          N'CONTROL SERVER',
                          N'ALTER ANY LOGIN',
                          N'IMPERSONATE ANY LOGIN',
                          N'ADMINISTER BULK OPERATIONS',
                          N'ALTER SETTINGS');
                    """,
                    "risky_public_server_permissions",
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.PublicDatabasePermissions => await RunCountRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT COUNT_BIG(*)
                    FROM sys.database_permissions AS permission
                    INNER JOIN sys.database_principals AS principal
                      ON principal.principal_id = permission.grantee_principal_id
                    WHERE principal.name = N'public'
                      AND permission.state IN (N'G', N'W')
                      AND permission.permission_name IN (
                          N'CONTROL',
                          N'ALTER',
                          N'TAKE OWNERSHIP',
                          N'IMPERSONATE',
                          N'EXECUTE');
                    """,
                    "risky_public_database_permissions",
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.DatabaseOwner => await RunCountRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT COUNT_BIG(*)
                    FROM sys.databases
                    WHERE database_id > 4
                      AND (
                          SUSER_SNAME(owner_sid) = N'sa'
                          OR IS_SRVROLEMEMBER(N'sysadmin', SUSER_SNAME(owner_sid)) = 1);
                    """,
                    "databases_owned_by_sysadmin_login",
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.Tde => await RunBooleanRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT CASE WHEN EXISTS (
                        SELECT 1
                        FROM sys.databases
                        WHERE name = DB_NAME()
                          AND is_encrypted = 0
                    ) THEN 1 ELSE 0 END;
                    """,
                    "database_encryption_disabled",
                    "database_encryption_enabled",
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.BackupEncryption => await RunBackupEncryptionCheckAsync(
                    connection,
                    script,
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.ServerPermissions => await RunCountRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT COUNT_BIG(*)
                    FROM sys.server_permissions AS permission
                    INNER JOIN sys.server_principals AS principal
                      ON principal.principal_id = permission.grantee_principal_id
                    WHERE principal.type IN (N'S', N'U', N'G')
                      AND principal.name NOT LIKE N'##%##'
                      AND permission.state IN (N'G', N'W')
                      AND permission.permission_name IN (
                          N'CONTROL SERVER',
                          N'ALTER ANY LOGIN',
                          N'IMPERSONATE ANY LOGIN',
                          N'ALTER SETTINGS');
                    """,
                    "excessive_server_permissions",
                    commandTimeoutSeconds,
                    cancellationToken),
                MsSqlProbeScripts.DatabasePermissions => await RunCountRiskCheckAsync(
                    connection,
                    script,
                    """
                    SELECT COUNT_BIG(*)
                    FROM sys.database_permissions AS permission
                    INNER JOIN sys.database_principals AS principal
                      ON principal.principal_id = permission.grantee_principal_id
                    WHERE principal.principal_id > 4
                      AND permission.state IN (N'G', N'W')
                      AND permission.permission_name IN (
                          N'CONTROL',
                          N'ALTER',
                          N'TAKE OWNERSHIP',
                          N'IMPERSONATE');
                    """,
                    "excessive_database_permissions",
                    commandTimeoutSeconds,
                    cancellationToken),
                _ => CreateResult(
                    script,
                    ProbeResultStatus.NotSupported,
                    "unknown_authenticated_probe",
                    exitCode: 0)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsHandledProviderException(exception))
        {
            var (status, reason) = ClassifyQueryFailure(exception);
            logger.LogWarning(
                "Authenticated Microsoft SQL Server check {Check} failed for target database with category {Category}.",
                script.NseScriptName,
                reason);

            return CreateResult(script, status, reason, exitCode: 1);
        }
    }

    private static async Task<ScanScriptExecutionResult> RunVersionAsync(
        SqlConnection connection,
        NseScript script,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            """
            SELECT
                CONVERT(nvarchar(128), SERVERPROPERTY(N'ProductVersion')),
                CONVERT(nvarchar(128), SERVERPROPERTY(N'ProductLevel')),
                CONVERT(nvarchar(256), SERVERPROPERTY(N'Edition')),
                SUSER_SNAME(),
                DB_NAME();
            """,
            commandTimeoutSeconds);
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SingleRow,
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Microsoft SQL Server metadata query returned no rows.");
        }

        var evidence = string.Join(
            ",",
            "product=Microsoft SQL Server",
            $"product_version={Normalize(reader.GetString(0))}",
            $"product_level={Normalize(reader.GetString(1))}",
            $"edition={Normalize(reader.GetString(2))}",
            $"session_login={Normalize(reader.GetString(3))}",
            $"database={Normalize(reader.GetString(4))}");
        return CreateResult(script, ProbeResultStatus.Passed, evidence, exitCode: 0);
    }

    private static async Task<ScanScriptExecutionResult> RunConfigurationCheckAsync(
        SqlConnection connection,
        NseScript script,
        string configurationName,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            """
            SELECT CONVERT(int, value_in_use)
            FROM sys.configurations
            WHERE name = @configurationName;
            """,
            commandTimeoutSeconds);
        command.Parameters.Add(new SqlParameter("@configurationName", SqlDbType.NVarChar, 128)
        {
            Value = configurationName
        });
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null or DBNull)
        {
            return CreateResult(
                script,
                ProbeResultStatus.NotSupported,
                "configuration_not_available",
                exitCode: 0);
        }

        var enabled = Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;
        return CreateResult(
            script,
            enabled ? ProbeResultStatus.Failed : ProbeResultStatus.Passed,
            enabled ? "configuration_enabled" : "configuration_disabled",
            exitCode: 0);
    }

    private static async Task<ScanScriptExecutionResult> RunBooleanRiskCheckAsync(
        SqlConnection connection,
        NseScript script,
        string query,
        string riskyEvidence,
        string safeEvidence,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var value = await ExecuteInt64Async(
            connection,
            query,
            commandTimeoutSeconds,
            cancellationToken);
        var isRisky = value > 0;
        return CreateResult(
            script,
            isRisky ? ProbeResultStatus.Failed : ProbeResultStatus.Passed,
            isRisky ? riskyEvidence : safeEvidence,
            exitCode: 0);
    }

    private static async Task<ScanScriptExecutionResult> RunCountRiskCheckAsync(
        SqlConnection connection,
        NseScript script,
        string query,
        string evidenceKey,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var count = await ExecuteInt64Async(
            connection,
            query,
            commandTimeoutSeconds,
            cancellationToken);
        return CreateResult(
            script,
            count > 0 ? ProbeResultStatus.Failed : ProbeResultStatus.Passed,
            $"{evidenceKey}={count}",
            exitCode: 0);
    }

    private static async Task<ScanScriptExecutionResult> RunThresholdRiskCheckAsync(
        SqlConnection connection,
        NseScript script,
        string query,
        long threshold,
        string evidenceKey,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var count = await ExecuteInt64Async(
            connection,
            query,
            commandTimeoutSeconds,
            cancellationToken);
        return CreateResult(
            script,
            count > threshold ? ProbeResultStatus.Failed : ProbeResultStatus.Passed,
            $"{evidenceKey}={count},threshold={threshold}",
            exitCode: 0);
    }

    private static async Task<ScanScriptExecutionResult> RunInverseCountRiskCheckAsync(
        SqlConnection connection,
        NseScript script,
        string query,
        string evidenceKey,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var count = await ExecuteInt64Async(
            connection,
            query,
            commandTimeoutSeconds,
            cancellationToken);
        return CreateResult(
            script,
            count == 0 ? ProbeResultStatus.Failed : ProbeResultStatus.Passed,
            $"{evidenceKey}={count}",
            exitCode: 0);
    }

    private static async Task<ScanScriptExecutionResult> RunBackupEncryptionCheckAsync(
        SqlConnection connection,
        NseScript script,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            connection,
            """
            SELECT TOP (1)
                CASE WHEN encryptor_thumbprint IS NULL THEN 0 ELSE 1 END
            FROM msdb.dbo.backupset
            WHERE database_name = DB_NAME()
              AND type = N'D'
            ORDER BY backup_finish_date DESC;
            """,
            commandTimeoutSeconds);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null or DBNull)
        {
            return CreateResult(
                script,
                ProbeResultStatus.Skipped,
                "no_full_backup_metadata",
                exitCode: 0);
        }

        var encrypted = Convert.ToInt32(value, CultureInfo.InvariantCulture) == 1;
        return CreateResult(
            script,
            encrypted ? ProbeResultStatus.Passed : ProbeResultStatus.Failed,
            encrypted ? "latest_full_backup_encrypted" : "latest_full_backup_not_encrypted",
            exitCode: 0);
    }

    private static async Task<long> ExecuteInt64Async(
        SqlConnection connection,
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

    private static SqlCommand CreateCommand(
        SqlConnection connection,
        string query,
        int commandTimeoutSeconds) =>
        new(query, connection)
        {
            CommandType = CommandType.Text,
            CommandTimeout = Math.Clamp(commandTimeoutSeconds, 1, 30)
        };

    private static ScanScriptExecutionResult CreateResult(
        NseScript script,
        ProbeResultStatus status,
        string evidence,
        int exitCode)
    {
        var checkName = script.NseScriptName.StartsWith("mssql-auth-", StringComparison.OrdinalIgnoreCase)
            ? script.NseScriptName["mssql-auth-".Length..]
            : script.NseScriptName;
        var output = string.Join(
            "; ",
            $"check={Normalize(checkName)}",
            $"status={ToToken(status)}",
            $"evidence={Normalize(evidence)}");
        var rawXml = new XElement(
            "script",
            new XAttribute("id", script.NseScriptName),
            new XAttribute("runner", "dbscanner-mssql-authenticated"),
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
        exception is SqlException
            or TimeoutException
            or InvalidOperationException;

    private static (ProbeResultStatus Status, string Reason) ClassifyConnectionFailure(Exception exception)
    {
        if (exception is TimeoutException)
        {
            return (ProbeResultStatus.ConnectionFailed, "connection_timeout");
        }

        if (exception is not SqlException sqlException)
        {
            return (ProbeResultStatus.ConnectionFailed, "unexpected_provider_error");
        }

        return sqlException.Number switch
        {
            -2 => (ProbeResultStatus.ConnectionFailed, "connection_timeout"),
            2 or 53 or 64 or 10060 or 11001 =>
                (ProbeResultStatus.ConnectionFailed, "host_unreachable"),
            18456 => (ProbeResultStatus.AuthenticationFailed, "authentication_failed"),
            4060 => (ProbeResultStatus.ConnectionFailed, "database_unavailable"),
            _ when sqlException.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase)
                || sqlException.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase)
                || sqlException.Message.Contains("TLS", StringComparison.OrdinalIgnoreCase) =>
                (ProbeResultStatus.ConnectionFailed, "tls_certificate_error"),
            _ => (ProbeResultStatus.ConnectionFailed, "unexpected_provider_error")
        };
    }

    private static (ProbeResultStatus Status, string Reason) ClassifyQueryFailure(Exception exception)
    {
        if (exception is SqlException sqlException
            && sqlException.Number is 229 or 297 or 300 or 916)
        {
            return (ProbeResultStatus.InsufficientPrivileges, "insufficient_privileges");
        }

        if (exception is SqlException { Number: -2 } or TimeoutException)
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
