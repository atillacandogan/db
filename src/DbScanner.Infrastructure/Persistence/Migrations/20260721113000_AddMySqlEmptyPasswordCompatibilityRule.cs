using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbScanner.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(DbScannerDbContext))]
    [Migration("20260721113000_AddMySqlEmptyPasswordCompatibilityRule")]
    public partial class AddMySqlEmptyPasswordCompatibilityRule : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO nse_scripts
                    ("Id", "NseScriptName", "DisplayName", "Description", "SupportedEngines",
                     "Categories", "DefaultEnabled", "RequiresCredentials", "IsIntrusive",
                     "AllowedArgsSchema", "ParserType", "MatcherType", "MatcherExpression",
                     "Severity", "Weight", "RemediationTemplate", "DocUrl", "ApprovalStatus",
                     "Source", "IsInstalled")
                VALUES
                    ('mysql-empty-password', 'mysql-empty-password', 'MySQL Empty Password',
                     'Checks whether MySQL accepts an anonymous or root login with an empty password. DbScanner automatically selects the compatible implementation for the advertised authentication plugin.',
                     'mysql', 'auth,intrusive', TRUE, FALSE, TRUE, NULL,
                     'GenericOutput', 'KeyValue', 'status=vulnerable', 'High', 5,
                     'Remove anonymous MySQL accounts, assign strong passwords, restrict account hosts, and keep caching_sha2_password enabled.',
                     NULL, 'Approved', 'Installed', TRUE)
                ON CONFLICT ("Id") DO UPDATE SET
                    "ParserType" = EXCLUDED."ParserType",
                    "MatcherType" = EXCLUDED."MatcherType",
                    "MatcherExpression" = EXCLUDED."MatcherExpression",
                    "Severity" = EXCLUDED."Severity",
                    "Weight" = EXCLUDED."Weight",
                    "RemediationTemplate" = EXCLUDED."RemediationTemplate",
                    "ApprovalStatus" = EXCLUDED."ApprovalStatus",
                    "DefaultEnabled" = EXCLUDED."DefaultEnabled",
                    "IsInstalled" = TRUE;

                INSERT INTO rules
                    ("Id", "Title", "Description", "Engine", "Severity", "Weight", "Enabled",
                     "ParserType", "MatcherType", "MatcherExpression", "Remediation")
                VALUES
                    ('mysql_empty_password_authentication',
                     'MySQL empty-password authentication accepted',
                     'The MySQL server accepted an anonymous or named account login with an empty password.',
                     'MySql', 'High', 5, TRUE, 'GenericOutput', 'KeyValue', 'status=vulnerable',
                     'Remove anonymous MySQL accounts, assign strong passwords, restrict account hosts, and keep caching_sha2_password enabled.')
                ON CONFLICT ("Id") DO UPDATE SET
                    "Title" = EXCLUDED."Title",
                    "Description" = EXCLUDED."Description",
                    "Engine" = EXCLUDED."Engine",
                    "Severity" = EXCLUDED."Severity",
                    "Weight" = EXCLUDED."Weight",
                    "Enabled" = TRUE,
                    "ParserType" = EXCLUDED."ParserType",
                    "MatcherType" = EXCLUDED."MatcherType",
                    "MatcherExpression" = EXCLUDED."MatcherExpression",
                    "Remediation" = EXCLUDED."Remediation";

                INSERT INTO rule_engine_support
                    ("Id", "RuleId", "Engine", "UnsupportedStatus")
                VALUES
                    ('34b9aa95-3305-4ee1-b695-45b274565ca8',
                     'mysql_empty_password_authentication', 'MySql', 'NotSupported')
                ON CONFLICT ("RuleId", "Engine") DO UPDATE SET
                    "UnsupportedStatus" = EXCLUDED."UnsupportedStatus";

                INSERT INTO rule_nse_scripts ("RuleId", "NseScriptId")
                VALUES ('mysql_empty_password_authentication', 'mysql-empty-password')
                ON CONFLICT ("RuleId", "NseScriptId") DO NOTHING;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM rule_nse_scripts
                WHERE "RuleId" = 'mysql_empty_password_authentication'
                  AND "NseScriptId" = 'mysql-empty-password';

                DELETE FROM rule_engine_support
                WHERE "RuleId" = 'mysql_empty_password_authentication';

                DELETE FROM rules
                WHERE "Id" = 'mysql_empty_password_authentication';
                """);
        }
    }
}
