namespace DbScanner.Infrastructure.Scanning;

internal static class MsSqlProbeScripts
{
    internal const string BrowserDiscovery = "mssql-browser-discovery";
    internal const string Version = "mssql-auth-version";
    internal const string CurrentLoginSysadmin = "mssql-auth-current-login-sysadmin";
    internal const string LoginUserMapping = "mssql-auth-login-user-mapping";
    internal const string ExcessiveSysadmin = "mssql-auth-excessive-sysadmin";
    internal const string GuestAccess = "mssql-auth-guest-access";
    internal const string Trustworthy = "mssql-auth-trustworthy";
    internal const string XpCmdShell = "mssql-auth-xp-cmdshell";
    internal const string OleAutomation = "mssql-auth-ole-automation";
    internal const string ClrEnabled = "mssql-auth-clr-enabled";
    internal const string AdHocDistributedQueries = "mssql-auth-ad-hoc-distributed-queries";
    internal const string CrossDatabaseOwnershipChaining = "mssql-auth-cross-db-ownership-chaining";
    internal const string MixedMode = "mssql-auth-mixed-mode";
    internal const string RiskyLogins = "mssql-auth-risky-logins";
    internal const string Audit = "mssql-auth-audit";
    internal const string PasswordPolicy = "mssql-auth-password-policy";
    internal const string PasswordExpiration = "mssql-auth-password-expiration";
    internal const string PublicServerPermissions = "mssql-auth-public-server-permissions";
    internal const string PublicDatabasePermissions = "mssql-auth-public-database-permissions";
    internal const string DatabaseOwner = "mssql-auth-database-owner";
    internal const string Tde = "mssql-auth-tde";
    internal const string BackupEncryption = "mssql-auth-backup-encryption";
    internal const string ServerPermissions = "mssql-auth-server-permissions";
    internal const string DatabasePermissions = "mssql-auth-database-permissions";

    internal static bool IsAuthenticatedProbe(string scriptName) =>
        scriptName.StartsWith("mssql-auth-", StringComparison.OrdinalIgnoreCase);
}
