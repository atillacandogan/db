namespace DbScanner.Infrastructure.Scanning;

internal static class OracleProbeScripts
{
    internal const string TnsVersion = "oracle-tns-version";
    internal const string Version = "oracle-auth-version";
    internal const string SessionPrivileges = "oracle-auth-session-privileges";
    internal const string DbaUsers = "oracle-auth-dba-users";
    internal const string RiskyUsers = "oracle-auth-risky-users";
    internal const string UnlockedUnusedAccounts = "oracle-auth-unlocked-unused-accounts";
    internal const string PasswordProfiles = "oracle-auth-password-profiles";
    internal const string AuditConfiguration = "oracle-auth-audit-configuration";
    internal const string RiskySystemPrivileges = "oracle-auth-risky-system-privileges";
    internal const string PublicPrivileges = "oracle-auth-public-privileges";
    internal const string DatabaseMetadata = "oracle-auth-database-metadata";

    internal static bool IsAuthenticatedProbe(string scriptName) =>
        scriptName.StartsWith("oracle-auth-", StringComparison.OrdinalIgnoreCase);
}
