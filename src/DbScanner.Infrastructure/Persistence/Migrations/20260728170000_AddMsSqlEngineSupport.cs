using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbScanner.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(DbScannerDbContext))]
    [Migration("20260728170000_AddMsSqlEngineSupport")]
    public partial class AddMsSqlEngineSupport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE nse_scripts
                SET "SupportedEngines" = 'generic,postgresql,mysql,mssql',
                    "IsInstalled" = TRUE
                WHERE "Id" = 'banner';

                INSERT INTO targets
                    ("Id", "Name", "Host", "Port", "Engine", "ConnectionMode",
                     "EnvironmentTag", "Enabled", "CreatedAt")
                VALUES
                    ('8c92a3b6-0e2c-46e7-9776-1ea8e92d46b7',
                     'Demo MSSQL',
                     'demo-mssql-target',
                     1433,
                     'MsSql',
                     'DefaultValues',
                     'demo',
                     TRUE,
                     TIMESTAMPTZ '2026-07-28 14:00:00+00')
                ON CONFLICT ("Host", "Port", "Engine") DO NOTHING;

                INSERT INTO scan_scope_allowlist
                    ("Id", "ScopeValue", "ScopeType", "Port", "Description", "Enabled", "CreatedAt")
                VALUES
                    ('05ea96e7-873e-4cf8-84b8-6253e3b3d70e',
                     'demo-mssql-target',
                     'DemoTarget',
                     1433,
                     'Aspire demo MSSQL target',
                     TRUE,
                     TIMESTAMPTZ '2026-07-28 14:00:00+00')
                ON CONFLICT ("ScopeValue", "Port") DO NOTHING;

                INSERT INTO nse_scripts
                    ("Id", "NseScriptName", "DisplayName", "Description", "SupportedEngines",
                     "Categories", "DefaultEnabled", "RequiresCredentials", "IsIntrusive",
                     "AllowedArgsSchema", "ParserType", "MatcherType", "MatcherExpression",
                     "Severity", "Weight", "RemediationTemplate", "DocUrl", "ApprovalStatus",
                     "Source", "IsInstalled")
                VALUES
                    ('ms-sql-info', 'ms-sql-info', 'MSSQL Info',
                     'Collects safe Microsoft SQL Server version and instance metadata.',
                     'mssql', 'safe,discovery', TRUE, FALSE, FALSE, NULL,
                     'GenericOutput', 'Contains', 'Microsoft SQL Server',
                     'Info', 0, NULL, NULL, 'Approved', 'Installed', TRUE)
                ON CONFLICT ("Id") DO UPDATE SET
                    "SupportedEngines" = EXCLUDED."SupportedEngines",
                    "Categories" = EXCLUDED."Categories",
                    "DefaultEnabled" = EXCLUDED."DefaultEnabled",
                    "RequiresCredentials" = EXCLUDED."RequiresCredentials",
                    "IsIntrusive" = EXCLUDED."IsIntrusive",
                    "ApprovalStatus" = EXCLUDED."ApprovalStatus",
                    "IsInstalled" = TRUE;

                INSERT INTO rules
                    ("Id", "Title", "Description", "Engine", "Severity", "Weight", "Enabled",
                     "ParserType", "MatcherType", "MatcherExpression", "Remediation")
                VALUES
                    ('mssql_port_open',
                     'MSSQL port open',
                     'The Microsoft SQL Server service port is reachable.',
                     'MsSql', 'Info', 0, TRUE, 'StructuredXml', 'KeyValue', 'state=open',
                     'Confirm the MSSQL service is expected to be reachable from the scanner network.'),
                    ('mssql_service_detected',
                     'MSSQL service detected',
                     'A Microsoft SQL Server service is detected on the target.',
                     'MsSql', 'Info', 0, TRUE, 'StructuredXml', 'Contains', 'ms-sql-s',
                     'Confirm the MSSQL exposure is expected and restricted to approved networks.'),
                    ('mssql_fingerprint_detected',
                     'MSSQL service fingerprint detected',
                     'Microsoft SQL Server version or instance metadata is exposed.',
                     'MsSql', 'Low', 1, TRUE, 'GenericOutput', 'Contains', 'Microsoft SQL Server',
                     'Limit unauthenticated service metadata exposure and restrict MSSQL access to approved networks.')
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

                INSERT INTO rule_engine_support
                    ("Id", "RuleId", "Engine", "UnsupportedStatus")
                VALUES
                    ('8449dff0-1e5a-42af-b593-b14b0cba959d',
                     'generic_db_port_open', 'MsSql', 'NotSupported'),
                    ('0fd254e1-2366-47cf-ae8f-7a7038790336',
                     'generic_service_fingerprint_detected', 'MsSql', 'NotSupported'),
                    ('ad3ebcee-c330-42c4-827d-e3e3d70f47f1',
                     'mssql_port_open', 'MsSql', 'NotSupported'),
                    ('5b6637fa-3214-4cec-b6f1-20b1de774129',
                     'mssql_service_detected', 'MsSql', 'NotSupported'),
                    ('10556843-af29-452d-af8c-4d455777ae3b',
                     'mssql_fingerprint_detected', 'MsSql', 'NotSupported')
                ON CONFLICT ("RuleId", "Engine") DO UPDATE SET
                    "UnsupportedStatus" = EXCLUDED."UnsupportedStatus";

                INSERT INTO rule_nse_scripts ("RuleId", "NseScriptId")
                VALUES
                    ('mssql_port_open', 'banner'),
                    ('mssql_service_detected', 'ms-sql-info'),
                    ('mssql_fingerprint_detected', 'ms-sql-info')
                ON CONFLICT ("RuleId", "NseScriptId") DO NOTHING;

                INSERT INTO nse_script_matchers
                    ("Id", "NseScriptId", "RuleId", "ParserType", "MatcherType",
                     "MatcherExpression", "Engine", "Enabled")
                VALUES
                    ('c96b40bb-bd70-422b-b335-9fb1b724ab67',
                     'banner', 'mssql_port_open', 'StructuredXml', 'KeyValue',
                     'state=open', 'MsSql', TRUE),
                    ('71baac99-775b-47a7-a07a-9396215f6d4d',
                     'ms-sql-info', 'mssql_service_detected', 'StructuredXml', 'Contains',
                     'ms-sql-s', 'MsSql', TRUE),
                    ('060e99a7-eddd-4d09-a9e0-f35f518ff33e',
                     'ms-sql-info', 'mssql_fingerprint_detected', 'GenericOutput', 'Contains',
                     'Microsoft SQL Server', 'MsSql', TRUE)
                ON CONFLICT ("Id") DO UPDATE SET
                    "ParserType" = EXCLUDED."ParserType",
                    "MatcherType" = EXCLUDED."MatcherType",
                    "MatcherExpression" = EXCLUDED."MatcherExpression",
                    "Engine" = EXCLUDED."Engine",
                    "Enabled" = EXCLUDED."Enabled";
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM nse_script_matchers
                WHERE "Id" IN (
                    'c96b40bb-bd70-422b-b335-9fb1b724ab67',
                    '71baac99-775b-47a7-a07a-9396215f6d4d',
                    '060e99a7-eddd-4d09-a9e0-f35f518ff33e');

                DELETE FROM rule_nse_scripts
                WHERE "RuleId" IN (
                    'mssql_port_open',
                    'mssql_service_detected',
                    'mssql_fingerprint_detected');

                DELETE FROM rule_engine_support
                WHERE "Engine" = 'MsSql';

                DELETE FROM rules
                WHERE "Id" IN (
                    'mssql_port_open',
                    'mssql_service_detected',
                    'mssql_fingerprint_detected');

                DELETE FROM nse_scripts
                WHERE "Id" = 'ms-sql-info';

                UPDATE nse_scripts
                SET "SupportedEngines" = 'generic,postgresql,mysql'
                WHERE "Id" = 'banner';

                DELETE FROM scan_scope_allowlist
                WHERE "Id" = '05ea96e7-873e-4cf8-84b8-6253e3b3d70e';

                DELETE FROM targets
                WHERE "Id" = '8c92a3b6-0e2c-46e7-9776-1ea8e92d46b7';
                """);
        }
    }
}
