using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbScanner.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DbScannerDbContext))]
[Migration("20260731140000_AddOracleEngineSupport")]
public partial class AddOracleEngineSupport : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            UPDATE nse_scripts
            SET "SupportedEngines" =
              CASE
                WHEN "SupportedEngines" LIKE '%oracle%' THEN "SupportedEngines"
                ELSE "SupportedEngines" || ',oracle'
              END
            WHERE "Id" = 'banner';

            INSERT INTO targets
              ("Id", "Name", "Host", "Port", "Engine", "ConnectionMode",
               "EnvironmentTag", "ProtectedCredentials", "Enabled", "CreatedAt")
            VALUES
              ('a1e6596a-5ef4-481d-a76b-813c84bfd0a5', 'Demo Oracle Database',
               'demo-oracle-target', 1521, 'Oracle', 'DefaultValues',
               'demo', NULL, TRUE, TIMESTAMPTZ '2026-07-31 14:00:00+00')
            ON CONFLICT ("Host", "Port", "Engine") DO NOTHING;

            INSERT INTO scan_scope_allowlist
              ("Id", "ScopeValue", "ScopeType", "Port", "Description", "Enabled", "CreatedAt")
            VALUES
              ('de24f19c-0d46-4de6-93df-2cc835e23d85', 'demo-oracle-target',
               'DemoTarget', 1521, 'Aspire demo Oracle Database target', TRUE,
               TIMESTAMPTZ '2026-07-31 14:00:00+00')
            ON CONFLICT ("ScopeValue", "Port") DO NOTHING;

            INSERT INTO nse_scripts
              ("Id", "NseScriptName", "DisplayName", "Description", "SupportedEngines",
               "Categories", "DefaultEnabled", "RequiresCredentials", "IsIntrusive",
               "AllowedArgsSchema", "ParserType", "MatcherType", "MatcherExpression",
               "Severity", "Weight", "RemediationTemplate", "DocUrl", "ApprovalStatus",
               "Source", "IsInstalled")
            VALUES
              ('oracle-tns-version', 'oracle-tns-version', 'Oracle Listener and TNS Version',
               'Safely collects Oracle TNS Listener version and protocol metadata without guessing credentials.',
               'oracle', 'safe,discovery,version', TRUE, FALSE, FALSE, NULL,
               'StructuredXml', 'Contains', 'Oracle', 'Low', 1,
               'Restrict Oracle Listener access to approved networks and limit unnecessary metadata exposure.',
               'https://nmap.org/nsedoc/scripts/oracle-tns-version.html', 'Approved', 'Installed', TRUE),
              ('oracle-auth-version', 'oracle-auth-version', 'Oracle Version and Edition',
               'Reads Oracle Database version and edition using the authorized scanner account.',
               'oracle', 'safe,auth,version', TRUE, TRUE, FALSE, NULL,
               'GenericOutput', 'Contains', 'status=passed', 'Info', 0, NULL, NULL, 'Approved', 'Manual', TRUE),
              ('oracle-auth-session-privileges', 'oracle-auth-session-privileges', 'Oracle Session Roles and Privileges',
               'Inventories roles and system privileges enabled for the authorized scanner session.',
               'oracle', 'safe,auth,authorization', TRUE, TRUE, FALSE, NULL,
               'GenericOutput', 'Contains', 'status=passed', 'Info', 0, NULL, NULL, 'Approved', 'Manual', TRUE),
              ('oracle-auth-dba-users', 'oracle-auth-dba-users', 'Oracle DBA Users',
               'Checks for non-system principals granted the DBA role.',
               'oracle', 'safe,auth,authorization', TRUE, TRUE, FALSE, NULL,
               'GenericOutput', 'Contains', 'status=failed', 'High', 3, NULL, NULL, 'Approved', 'Manual', TRUE),
              ('oracle-auth-risky-users', 'oracle-auth-risky-users', 'Oracle Default or Risky Users',
               'Checks for unlocked default, sample, monitoring or legacy accounts.',
               'oracle', 'safe,auth,accounts', TRUE, TRUE, FALSE, NULL,
               'GenericOutput', 'Contains', 'status=failed', 'High', 3, NULL, NULL, 'Approved', 'Manual', TRUE),
              ('oracle-auth-unlocked-unused-accounts', 'oracle-auth-unlocked-unused-accounts', 'Oracle Unlocked Unused Accounts',
               'Checks for unlocked Oracle-maintained accounts with no recent login.',
               'oracle', 'safe,auth,accounts', TRUE, TRUE, FALSE, NULL,
               'GenericOutput', 'Contains', 'status=failed', 'Medium', 2, NULL, NULL, 'Approved', 'Manual', TRUE),
              ('oracle-auth-password-profiles', 'oracle-auth-password-profiles', 'Oracle Password Profiles',
               'Checks password profiles for unlimited or default weak limits.',
               'oracle', 'safe,auth,password-policy', TRUE, TRUE, FALSE, NULL,
               'GenericOutput', 'Contains', 'status=failed', 'High', 3, NULL, NULL, 'Approved', 'Manual', TRUE),
              ('oracle-auth-audit-configuration', 'oracle-auth-audit-configuration', 'Oracle Audit Configuration',
               'Checks whether visible unified audit policies are enabled.',
               'oracle', 'safe,auth,audit', TRUE, TRUE, FALSE, NULL,
               'GenericOutput', 'Contains', 'status=failed', 'Medium', 2, NULL, NULL, 'Approved', 'Manual', TRUE),
              ('oracle-auth-risky-system-privileges', 'oracle-auth-risky-system-privileges', 'Oracle Risky System Privileges',
               'Checks for high-impact system privileges granted to non-system principals.',
               'oracle', 'safe,auth,authorization', TRUE, TRUE, FALSE, NULL,
               'GenericOutput', 'Contains', 'status=failed', 'High', 3, NULL, NULL, 'Approved', 'Manual', TRUE),
              ('oracle-auth-public-privileges', 'oracle-auth-public-privileges', 'Oracle PUBLIC Risky Privileges',
               'Checks high-risk system and package privileges granted to PUBLIC.',
               'oracle', 'safe,auth,authorization', TRUE, TRUE, FALSE, NULL,
               'GenericOutput', 'Contains', 'status=failed', 'High', 3, NULL, NULL, 'Approved', 'Manual', TRUE),
              ('oracle-auth-database-metadata', 'oracle-auth-database-metadata', 'Oracle Database Metadata',
               'Reads database name, platform, log mode and open mode with the authorized scanner account.',
               'oracle', 'safe,auth,discovery', TRUE, TRUE, FALSE, NULL,
               'GenericOutput', 'Contains', 'status=passed', 'Info', 0, NULL, NULL, 'Approved', 'Manual', TRUE)
            ON CONFLICT ("Id") DO UPDATE SET
              "DisplayName" = EXCLUDED."DisplayName",
              "Description" = EXCLUDED."Description",
              "SupportedEngines" = EXCLUDED."SupportedEngines",
              "Categories" = EXCLUDED."Categories",
              "DefaultEnabled" = EXCLUDED."DefaultEnabled",
              "RequiresCredentials" = EXCLUDED."RequiresCredentials",
              "IsIntrusive" = EXCLUDED."IsIntrusive",
              "ParserType" = EXCLUDED."ParserType",
              "MatcherType" = EXCLUDED."MatcherType",
              "MatcherExpression" = EXCLUDED."MatcherExpression",
              "Severity" = EXCLUDED."Severity",
              "Weight" = EXCLUDED."Weight",
              "ApprovalStatus" = EXCLUDED."ApprovalStatus",
              "IsInstalled" = TRUE;

            INSERT INTO rules
              ("Id", "Title", "Description", "Engine", "Severity", "Weight", "Enabled",
               "ParserType", "MatcherType", "MatcherExpression", "Remediation")
            VALUES
              ('oracle_port_open', 'Oracle Database port open',
               'The Oracle Listener port is reachable.', 'Oracle', 'Info', 0, TRUE,
               'StructuredXml', 'KeyValue', 'state=open',
               'Confirm the Oracle Listener is expected to be reachable from the scanner network.'),
              ('oracle_service_detected', 'Oracle Database service detected',
               'An Oracle TNS Listener service is detected.', 'Oracle', 'Info', 0, TRUE,
               'StructuredXml', 'Contains', 'service=oracle-tns',
               'Restrict Oracle Listener access to approved application and administration networks.'),
              ('oracle_listener_metadata_exposed', 'Oracle Listener metadata exposed',
               'The Listener returned Oracle TNS version or service metadata without database authentication.',
               'Oracle', 'Low', 1, TRUE, 'GenericOutput', 'Contains', 'version',
               'Restrict Listener access and reduce unauthenticated metadata exposure where operationally possible.'),
              ('oracle_non_system_dba_users', 'Non-system users have DBA role',
               'One or more non-system principals have the DBA role.', 'Oracle', 'High', 3, TRUE,
               'GenericOutput', 'Contains', 'status=failed',
               'Remove unnecessary DBA role grants and apply least privilege.'),
              ('oracle_risky_users_open', 'Default or risky Oracle users are unlocked',
               'A default, sample, monitoring or legacy account is open.', 'Oracle', 'High', 3, TRUE,
               'GenericOutput', 'Contains', 'status=failed',
               'Lock or remove unused default accounts and rotate credentials for required accounts.'),
              ('oracle_unused_accounts_open', 'Unused Oracle-maintained accounts are unlocked',
               'An Oracle-maintained account is open and has no recent login.', 'Oracle', 'Medium', 2, TRUE,
               'GenericOutput', 'Contains', 'status=failed',
               'Review and lock unnecessary Oracle-maintained accounts.'),
              ('oracle_weak_password_profiles', 'Weak Oracle password profile settings',
               'A password profile has unlimited or default weak security limits.', 'Oracle', 'High', 3, TRUE,
               'GenericOutput', 'Contains', 'status=failed',
               'Set explicit failed-login, lifetime and password verification controls.'),
              ('oracle_auditing_insufficient', 'Oracle auditing disabled or insufficient',
               'No enabled unified audit policy was visible to the scanner account.', 'Oracle', 'Medium', 2, TRUE,
               'GenericOutput', 'Contains', 'status=failed',
               'Enable and monitor appropriate unified audit policies.'),
              ('oracle_risky_system_privileges', 'Risky Oracle system privilege grants',
               'A non-system principal has a high-impact system privilege.', 'Oracle', 'High', 3, TRUE,
               'GenericOutput', 'Contains', 'status=failed',
               'Revoke excessive system privileges and grant narrowly scoped roles.'),
              ('oracle_public_risky_privileges', 'Risky Oracle privileges granted to PUBLIC',
               'PUBLIC has a high-risk system or package privilege.', 'Oracle', 'High', 3, TRUE,
               'GenericOutput', 'Contains', 'status=failed',
               'Revoke risky PUBLIC grants and grant privileges only to explicit application roles.')
            ON CONFLICT ("Id") DO UPDATE SET
              "Title" = EXCLUDED."Title",
              "Description" = EXCLUDED."Description",
              "Engine" = EXCLUDED."Engine",
              "Severity" = EXCLUDED."Severity",
              "Weight" = EXCLUDED."Weight",
              "Enabled" = EXCLUDED."Enabled",
              "ParserType" = EXCLUDED."ParserType",
              "MatcherType" = EXCLUDED."MatcherType",
              "MatcherExpression" = EXCLUDED."MatcherExpression",
              "Remediation" = EXCLUDED."Remediation";

            INSERT INTO rule_engine_support ("Id", "RuleId", "Engine", "UnsupportedStatus")
            VALUES
              ('b61c318a-c278-41d4-84c0-82cf735e9354', 'generic_db_port_open', 'Oracle', 'NotSupported'),
              ('2e2fb49b-24b3-416b-8566-b06bc0934ce0', 'generic_service_fingerprint_detected', 'Oracle', 'NotSupported'),
              ('52ff0095-9b2c-42ec-ac8b-7612a9bde122', 'oracle_port_open', 'Oracle', 'NotSupported'),
              ('f45ff0ba-6328-4db9-b9fe-a0abc98b0efa', 'oracle_service_detected', 'Oracle', 'NotSupported'),
              ('f028ff71-072f-47a5-a822-20464fc6443f', 'oracle_listener_metadata_exposed', 'Oracle', 'NotSupported'),
              ('34d5cd44-7ab4-4233-997c-dc9a13fcb37b', 'oracle_non_system_dba_users', 'Oracle', 'NotSupported'),
              ('50ca2c32-4ed6-4bcd-bdc5-80fb339ffb10', 'oracle_risky_users_open', 'Oracle', 'NotSupported'),
              ('05cc5880-9c12-47f7-b918-b1b2b5c96e5a', 'oracle_unused_accounts_open', 'Oracle', 'NotSupported'),
              ('b6436f29-f48e-4585-b389-16149746bcd7', 'oracle_weak_password_profiles', 'Oracle', 'NotSupported'),
              ('c6178668-7098-4a46-8b25-391dca499201', 'oracle_auditing_insufficient', 'Oracle', 'NotSupported'),
              ('b6ed29df-a8f8-4eae-9af7-e7a9f40644e2', 'oracle_risky_system_privileges', 'Oracle', 'NotSupported'),
              ('a369a87e-7c4e-43df-94ae-aac106fac7aa', 'oracle_public_risky_privileges', 'Oracle', 'NotSupported')
            ON CONFLICT ("RuleId", "Engine") DO UPDATE SET
              "UnsupportedStatus" = EXCLUDED."UnsupportedStatus";

            INSERT INTO rule_nse_scripts ("RuleId", "NseScriptId")
            VALUES
              ('oracle_port_open', 'banner'),
              ('oracle_service_detected', 'oracle-tns-version'),
              ('oracle_listener_metadata_exposed', 'oracle-tns-version'),
              ('oracle_non_system_dba_users', 'oracle-auth-dba-users'),
              ('oracle_risky_users_open', 'oracle-auth-risky-users'),
              ('oracle_unused_accounts_open', 'oracle-auth-unlocked-unused-accounts'),
              ('oracle_weak_password_profiles', 'oracle-auth-password-profiles'),
              ('oracle_auditing_insufficient', 'oracle-auth-audit-configuration'),
              ('oracle_risky_system_privileges', 'oracle-auth-risky-system-privileges'),
              ('oracle_public_risky_privileges', 'oracle-auth-public-privileges')
            ON CONFLICT ("RuleId", "NseScriptId") DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM rule_nse_scripts WHERE "RuleId" LIKE 'oracle_%';
            DELETE FROM rule_engine_support WHERE "Engine" = 'Oracle';
            DELETE FROM rules WHERE "Id" LIKE 'oracle_%';
            DELETE FROM nse_scripts
              WHERE "Id" = 'oracle-tns-version' OR "Id" LIKE 'oracle-auth-%';
            UPDATE nse_scripts
            SET "SupportedEngines" = REPLACE("SupportedEngines", ',oracle', '')
            WHERE "Id" = 'banner';
            DELETE FROM scan_scope_allowlist
            WHERE "Id" = 'de24f19c-0d46-4de6-93df-2cc835e23d85';
            DELETE FROM targets
            WHERE "Id" = 'a1e6596a-5ef4-481d-a76b-813c84bfd0a5';
            """);
    }
}
