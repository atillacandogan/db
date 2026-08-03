using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbScanner.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DbScannerDbContext))]
[Migration("20260731100000_CompleteMsSqlEngineSupport")]
public partial class CompleteMsSqlEngineSupport : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ResultStatus",
            table: "scan_script_results",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE targets
            SET "Name" = 'Demo Microsoft SQL Server'
            WHERE "Id" = '8c92a3b6-0e2c-46e7-9776-1ea8e92d46b7'
              AND "Name" = 'Demo MSSQL';

            UPDATE nse_scripts
            SET "DisplayName" = REPLACE("DisplayName", 'MSSQL', 'Microsoft SQL Server')
            WHERE "SupportedEngines" LIKE '%mssql%'
              AND "DisplayName" LIKE '%MSSQL%';

            UPDATE rules
            SET "Title" = REPLACE("Title", 'MSSQL', 'Microsoft SQL Server'),
                "Description" = REPLACE("Description", 'MSSQL', 'Microsoft SQL Server'),
                "Remediation" = REPLACE("Remediation", 'MSSQL', 'Microsoft SQL Server')
            WHERE "Engine" = 'MsSql';

            INSERT INTO nse_scripts
                ("Id", "NseScriptName", "DisplayName", "Description", "SupportedEngines",
                 "Categories", "DefaultEnabled", "RequiresCredentials", "IsIntrusive",
                 "AllowedArgsSchema", "ParserType", "MatcherType", "MatcherExpression",
                 "Severity", "Weight", "RemediationTemplate", "DocUrl", "ApprovalStatus",
                 "Source", "IsInstalled")
            VALUES
                ('mssql-browser-discovery', 'mssql-browser-discovery', 'Microsoft SQL Server Browser Discovery',
                 'Safely checks whether the SQL Server Browser service responds on UDP 1434.',
                 'mssql', 'safe,discovery', TRUE, FALSE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'Low', 1,
                 'Restrict UDP 1434 to approved administration networks or disable SQL Server Browser when it is not required.',
                 NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-version', 'mssql-auth-version', 'Microsoft SQL Server Authenticated Version',
                 'Reads SQL Server product version, product level, edition, session login and database through an authorized read-only connection.',
                 'mssql', 'safe,auth,version', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=passed', 'Info', 0, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-current-login-sysadmin', 'mssql-auth-current-login-sysadmin', 'Microsoft SQL Server Current Login Sysadmin',
                 'Checks whether the supplied scanner login is a sysadmin.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'High', 3, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-login-user-mapping', 'mssql-auth-login-user-mapping', 'Microsoft SQL Server Login User Mapping',
                 'Checks the current database for orphaned SQL users.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'Medium', 2, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-excessive-sysadmin', 'mssql-auth-excessive-sysadmin', 'Microsoft SQL Server Excessive Sysadmin Membership',
                 'Counts enabled members of the sysadmin fixed server role.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'High', 3, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-guest-access', 'mssql-auth-guest-access', 'Microsoft SQL Server Guest Access',
                 'Checks whether CONNECT is granted to guest in the selected database.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'Medium', 2, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-trustworthy', 'mssql-auth-trustworthy', 'Microsoft SQL Server Trustworthy Database',
                 'Checks whether the selected database has TRUSTWORTHY enabled.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'High', 3, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-xp-cmdshell', 'mssql-auth-xp-cmdshell', 'Microsoft SQL Server xp_cmdshell Configuration',
                 'Reads the in-use xp_cmdshell configuration without executing it.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'Critical', 4, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-ole-automation', 'mssql-auth-ole-automation', 'Microsoft SQL Server OLE Automation Configuration',
                 'Reads the in-use Ole Automation Procedures configuration.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'High', 3, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-clr-enabled', 'mssql-auth-clr-enabled', 'Microsoft SQL Server CLR Integration Configuration',
                 'Reads the in-use CLR integration configuration.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'Medium', 2, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-ad-hoc-distributed-queries', 'mssql-auth-ad-hoc-distributed-queries', 'Microsoft SQL Server Ad Hoc Distributed Queries',
                 'Reads the in-use Ad Hoc Distributed Queries configuration.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'High', 3, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-cross-db-ownership-chaining', 'mssql-auth-cross-db-ownership-chaining', 'Microsoft SQL Server Cross Database Ownership Chaining',
                 'Checks server and user-database ownership chaining settings.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'High', 3, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-mixed-mode', 'mssql-auth-mixed-mode', 'Microsoft SQL Server Mixed Authentication Mode',
                 'Evaluates whether SQL Server and Windows mixed authentication mode is enabled.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'Low', 1, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-risky-logins', 'mssql-auth-risky-logins', 'Microsoft SQL Server Risky Login Names',
                 'Checks for enabled SQL logins with common high-risk default names.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'Medium', 2, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-audit', 'mssql-auth-audit', 'Microsoft SQL Server Audit',
                 'Checks whether at least one SQL Server Audit is enabled.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'Medium', 2, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-password-policy', 'mssql-auth-password-policy', 'Microsoft SQL Server Login Password Policy',
                 'Checks enabled SQL logins for password policy enforcement metadata.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'High', 3, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-password-expiration', 'mssql-auth-password-expiration', 'Microsoft SQL Server Login Password Expiration',
                 'Checks enabled non-SA SQL logins for password expiration metadata.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'Medium', 2, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-public-server-permissions', 'mssql-auth-public-server-permissions', 'Microsoft SQL Server Public Server Permissions',
                 'Checks the public server role for high-risk grants.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'High', 3, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-public-database-permissions', 'mssql-auth-public-database-permissions', 'Microsoft SQL Server Public Database Permissions',
                 'Checks the public database role for high-risk grants.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'High', 3, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-database-owner', 'mssql-auth-database-owner', 'Microsoft SQL Server Database Owner',
                 'Checks user databases whose owner maps to a sysadmin login.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'High', 3, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-tde', 'mssql-auth-tde', 'Microsoft SQL Server Transparent Data Encryption',
                 'Checks encryption metadata for the selected database.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'Medium', 2, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-backup-encryption', 'mssql-auth-backup-encryption', 'Microsoft SQL Server Backup Encryption',
                 'Checks encryption metadata for the most recent full backup when accessible.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'Medium', 2, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-server-permissions', 'mssql-auth-server-permissions', 'Microsoft SQL Server Permissions (Server-Level)',
                 'Checks non-system principals for excessive server-level grants.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'High', 3, NULL, NULL, 'Approved', 'Manual', TRUE),
                ('mssql-auth-database-permissions', 'mssql-auth-database-permissions', 'Microsoft SQL Server Permissions (Database-Level)',
                 'Checks database principals for excessive database-level grants.',
                 'mssql', 'safe,auth', TRUE, TRUE, FALSE, NULL,
                 'GenericOutput', 'Contains', 'status=failed', 'High', 3, NULL, NULL, 'Approved', 'Manual', TRUE)
            ON CONFLICT ("Id") DO UPDATE SET
                "SupportedEngines" = EXCLUDED."SupportedEngines",
                "Categories" = EXCLUDED."Categories",
                "DefaultEnabled" = EXCLUDED."DefaultEnabled",
                "RequiresCredentials" = EXCLUDED."RequiresCredentials",
                "IsIntrusive" = EXCLUDED."IsIntrusive",
                "ApprovalStatus" = EXCLUDED."ApprovalStatus";

            INSERT INTO rules
                ("Id", "Title", "Description", "Engine", "Severity", "Weight", "Enabled",
                 "ParserType", "MatcherType", "MatcherExpression", "Remediation")
            VALUES
                ('mssql_browser_service_exposed', 'Microsoft SQL Server Browser service exposed',
                 'The SQL Server Browser service responded on UDP 1434.', 'MsSql', 'Low', 1, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Restrict UDP 1434 to approved networks or disable SQL Server Browser when it is not required.'),
                ('mssql_unsupported_version_detected', 'Unsupported or outdated Microsoft SQL Server version detected',
                 'The service fingerprint indicates a SQL Server major version older than SQL Server 2022.', 'MsSql', 'High', 3, TRUE,
                 'GenericOutput', 'Regex', 'version=(?:[0-9]|1[0-5])(?:\.|;)',
                 'Upgrade to a supported SQL Server release and current cumulative update.'),
                ('mssql_metadata_exposed', 'Microsoft SQL Server metadata exposed',
                 'Unauthenticated service detection returned SQL Server product metadata.', 'MsSql', 'Low', 1, TRUE,
                 'GenericOutput', 'Contains', 'source=nmap-service-detection',
                 'Restrict SQL Server exposure to approved scanner and application networks.'),
                ('mssql_current_login_sysadmin', 'Scanner login is sysadmin',
                 'The supplied scanner login is a member of the sysadmin fixed server role.', 'MsSql', 'High', 3, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Use a dedicated least-privilege scanner login without sysadmin membership.'),
                ('mssql_login_user_mapping_invalid', 'Orphaned database user detected',
                 'One or more SQL database users do not map to a server login.', 'MsSql', 'Medium', 2, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Remove obsolete users or repair the login-to-user SID mapping.'),
                ('mssql_excessive_sysadmin_membership', 'Excessive sysadmin membership',
                 'More than one enabled login is a member of sysadmin.', 'MsSql', 'High', 3, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Limit sysadmin membership to explicitly approved emergency administration accounts.'),
                ('mssql_guest_access_enabled', 'Guest access enabled',
                 'CONNECT is granted to guest in the scanned database.', 'MsSql', 'Medium', 2, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Revoke CONNECT from guest unless a documented application dependency requires it.'),
                ('mssql_trustworthy_database_enabled', 'TRUSTWORTHY database enabled',
                 'The scanned database has TRUSTWORTHY enabled.', 'MsSql', 'High', 3, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Disable TRUSTWORTHY and use signed modules or narrowly scoped permissions.'),
                ('mssql_xp_cmdshell_enabled', 'xp_cmdshell enabled',
                 'The xp_cmdshell server configuration is enabled.', 'MsSql', 'Critical', 4, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Disable xp_cmdshell unless a documented and tightly controlled dependency requires it.'),
                ('mssql_ole_automation_enabled', 'OLE Automation Procedures enabled',
                 'Ole Automation Procedures is enabled.', 'MsSql', 'High', 3, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Disable Ole Automation Procedures unless it is explicitly required and monitored.'),
                ('mssql_clr_integration_enabled', 'CLR integration enabled',
                 'SQL Server CLR integration is enabled.', 'MsSql', 'Medium', 2, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Disable CLR integration when unused and strictly control trusted assemblies when required.'),
                ('mssql_ad_hoc_distributed_queries_enabled', 'Ad Hoc Distributed Queries enabled',
                 'Ad Hoc Distributed Queries is enabled.', 'MsSql', 'High', 3, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Disable Ad Hoc Distributed Queries unless an approved use case requires it.'),
                ('mssql_cross_db_ownership_chaining_enabled', 'Cross database ownership chaining enabled',
                 'Server-level or user-database ownership chaining is enabled.', 'MsSql', 'High', 3, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Disable cross-database ownership chaining and use signed modules or explicit permissions.'),
                ('mssql_mixed_mode_enabled', 'Mixed authentication mode enabled',
                 'SQL Server and Windows mixed authentication mode is enabled.', 'MsSql', 'Low', 1, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Prefer Windows or federated authentication where possible and tightly govern SQL logins.'),
                ('mssql_risky_login_enabled', 'Risky default login enabled',
                 'An enabled SQL login uses a common high-risk administrative or test name.', 'MsSql', 'Medium', 2, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Disable or rename unnecessary default-style logins and apply least privilege.'),
                ('mssql_auditing_insufficient', 'SQL Server auditing disabled or insufficient',
                 'No enabled SQL Server Audit was visible to the scanner login.', 'MsSql', 'Medium', 2, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Enable SQL Server Audit with appropriate server and database audit specifications.'),
                ('mssql_login_password_policy_disabled', 'Login without password policy enforcement',
                 'An enabled SQL login does not enforce the Windows password policy.', 'MsSql', 'High', 3, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Enable CHECK_POLICY for SQL logins unless a documented exception exists.'),
                ('mssql_login_password_expiration_disabled', 'Login without password expiration',
                 'An enabled non-SA SQL login does not enforce password expiration.', 'MsSql', 'Medium', 2, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Enable CHECK_EXPIRATION where operationally appropriate or document compensating secret rotation.'),
                ('mssql_public_role_risky_permissions', 'Risky permissions granted to public role',
                 'The public server or database role has a high-risk permission grant.', 'MsSql', 'High', 3, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Revoke high-risk grants from public and grant only to dedicated least-privilege roles.'),
                ('mssql_database_owner_risky', 'Database owner mapped to risky login',
                 'A user database owner maps to a sysadmin login.', 'MsSql', 'High', 3, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Use a dedicated disabled owner login and avoid ownership by interactive sysadmin accounts.'),
                ('mssql_database_encryption_disabled', 'Database encryption not enabled',
                 'Transparent Data Encryption is not enabled for the scanned database.', 'MsSql', 'Medium', 2, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Evaluate TDE based on data classification and securely manage the encryption hierarchy and backups.'),
                ('mssql_backup_encryption_missing', 'Backup encryption not detected',
                 'The most recent visible full backup is not encrypted.', 'MsSql', 'Medium', 2, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Encrypt database backups and protect the associated certificates and keys.'),
                ('mssql_excessive_server_permissions', 'Excessive server-level permissions',
                 'A non-system principal has a high-risk server-level permission.', 'MsSql', 'High', 3, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Remove broad server permissions and grant narrowly scoped privileges through managed roles.'),
                ('mssql_excessive_database_permissions', 'Excessive database-level permissions',
                 'A database principal has a high-risk database-level permission.', 'MsSql', 'High', 3, TRUE,
                 'GenericOutput', 'Contains', 'status=failed',
                 'Remove broad database permissions and grant only the permissions required by the workload.')
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
            SELECT
                ('a1000000-0000-0000-0000-' || LPAD(ROW_NUMBER() OVER (ORDER BY "Id")::text, 12, '0'))::uuid,
                "Id",
                'MsSql',
                'NotSupported'
            FROM rules
            WHERE "Id" LIKE 'mssql_%'
            ON CONFLICT ("RuleId", "Engine") DO UPDATE SET
                "UnsupportedStatus" = EXCLUDED."UnsupportedStatus";

            INSERT INTO rule_nse_scripts ("RuleId", "NseScriptId")
            VALUES
                ('mssql_browser_service_exposed', 'mssql-browser-discovery'),
                ('mssql_unsupported_version_detected', 'ms-sql-info'),
                ('mssql_metadata_exposed', 'ms-sql-info'),
                ('mssql_current_login_sysadmin', 'mssql-auth-current-login-sysadmin'),
                ('mssql_login_user_mapping_invalid', 'mssql-auth-login-user-mapping'),
                ('mssql_excessive_sysadmin_membership', 'mssql-auth-excessive-sysadmin'),
                ('mssql_guest_access_enabled', 'mssql-auth-guest-access'),
                ('mssql_trustworthy_database_enabled', 'mssql-auth-trustworthy'),
                ('mssql_xp_cmdshell_enabled', 'mssql-auth-xp-cmdshell'),
                ('mssql_ole_automation_enabled', 'mssql-auth-ole-automation'),
                ('mssql_clr_integration_enabled', 'mssql-auth-clr-enabled'),
                ('mssql_ad_hoc_distributed_queries_enabled', 'mssql-auth-ad-hoc-distributed-queries'),
                ('mssql_cross_db_ownership_chaining_enabled', 'mssql-auth-cross-db-ownership-chaining'),
                ('mssql_mixed_mode_enabled', 'mssql-auth-mixed-mode'),
                ('mssql_risky_login_enabled', 'mssql-auth-risky-logins'),
                ('mssql_auditing_insufficient', 'mssql-auth-audit'),
                ('mssql_login_password_policy_disabled', 'mssql-auth-password-policy'),
                ('mssql_login_password_expiration_disabled', 'mssql-auth-password-expiration'),
                ('mssql_public_role_risky_permissions', 'mssql-auth-public-server-permissions'),
                ('mssql_public_role_risky_permissions', 'mssql-auth-public-database-permissions'),
                ('mssql_database_owner_risky', 'mssql-auth-database-owner'),
                ('mssql_database_encryption_disabled', 'mssql-auth-tde'),
                ('mssql_backup_encryption_missing', 'mssql-auth-backup-encryption'),
                ('mssql_excessive_server_permissions', 'mssql-auth-server-permissions'),
                ('mssql_excessive_database_permissions', 'mssql-auth-database-permissions')
            ON CONFLICT ("RuleId", "NseScriptId") DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM rule_nse_scripts
            WHERE "NseScriptId" = 'mssql-browser-discovery'
               OR "NseScriptId" LIKE 'mssql-auth-%';

            DELETE FROM rule_engine_support
            WHERE "RuleId" IN (
                SELECT "Id" FROM rules
                WHERE "Id" IN (
                    'mssql_browser_service_exposed',
                    'mssql_unsupported_version_detected',
                    'mssql_metadata_exposed',
                    'mssql_current_login_sysadmin',
                    'mssql_login_user_mapping_invalid',
                    'mssql_excessive_sysadmin_membership',
                    'mssql_guest_access_enabled',
                    'mssql_trustworthy_database_enabled',
                    'mssql_xp_cmdshell_enabled',
                    'mssql_ole_automation_enabled',
                    'mssql_clr_integration_enabled',
                    'mssql_ad_hoc_distributed_queries_enabled',
                    'mssql_cross_db_ownership_chaining_enabled',
                    'mssql_mixed_mode_enabled',
                    'mssql_risky_login_enabled',
                    'mssql_auditing_insufficient',
                    'mssql_login_password_policy_disabled',
                    'mssql_login_password_expiration_disabled',
                    'mssql_public_role_risky_permissions',
                    'mssql_database_owner_risky',
                    'mssql_database_encryption_disabled',
                    'mssql_backup_encryption_missing',
                    'mssql_excessive_server_permissions',
                    'mssql_excessive_database_permissions'));

            DELETE FROM rules
            WHERE "Id" IN (
                'mssql_browser_service_exposed',
                'mssql_unsupported_version_detected',
                'mssql_metadata_exposed',
                'mssql_current_login_sysadmin',
                'mssql_login_user_mapping_invalid',
                'mssql_excessive_sysadmin_membership',
                'mssql_guest_access_enabled',
                'mssql_trustworthy_database_enabled',
                'mssql_xp_cmdshell_enabled',
                'mssql_ole_automation_enabled',
                'mssql_clr_integration_enabled',
                'mssql_ad_hoc_distributed_queries_enabled',
                'mssql_cross_db_ownership_chaining_enabled',
                'mssql_mixed_mode_enabled',
                'mssql_risky_login_enabled',
                'mssql_auditing_insufficient',
                'mssql_login_password_policy_disabled',
                'mssql_login_password_expiration_disabled',
                'mssql_public_role_risky_permissions',
                'mssql_database_owner_risky',
                'mssql_database_encryption_disabled',
                'mssql_backup_encryption_missing',
                'mssql_excessive_server_permissions',
                'mssql_excessive_database_permissions');

            DELETE FROM nse_scripts
            WHERE "Id" = 'mssql-browser-discovery'
               OR "Id" LIKE 'mssql-auth-%';
            """);

        migrationBuilder.DropColumn(
            name: "ResultStatus",
            table: "scan_script_results");
    }
}
