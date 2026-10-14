using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GeneralizeRuleAndResultCheckIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScanCheckId",
                table: "scan_script_results",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScanCheckId",
                table: "rule_nse_scripts",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE rule_nse_scripts
                SET "ScanCheckId" = "NseScriptId"
                WHERE "ScanCheckId" IS NULL;

                UPDATE scan_script_results
                SET "ScanCheckId" = "NseScriptId"
                WHERE "ScanCheckId" IS NULL
                  AND "NseScriptId" IS NOT NULL;

                UPDATE nse_scripts
                SET "ExecutorType" = CASE
                    WHEN lower("NseScriptName") = 'mysql-empty-password' THEN 'mysql-native'
                    WHEN lower("NseScriptName") = 'mssql-browser-discovery'
                      OR lower("NseScriptName") LIKE 'mssql-auth-%' THEN 'mssql-native'
                    WHEN lower("NseScriptName") LIKE 'oracle-auth-%' THEN 'oracle-native'
                    ELSE 'nmap-nse'
                END
                WHERE "ExecutorType" IS NULL;
                """);

            migrationBuilder.UpdateData(
                table: "rule_nse_scripts",
                keyColumns: new[] { "NseScriptId", "RuleId" },
                keyValues: new object[] { "banner", "generic_db_port_open" },
                column: "ScanCheckId",
                value: "banner");

            migrationBuilder.UpdateData(
                table: "rule_nse_scripts",
                keyColumns: new[] { "NseScriptId", "RuleId" },
                keyValues: new object[] { "banner", "generic_service_fingerprint_detected" },
                column: "ScanCheckId",
                value: "banner");

            migrationBuilder.UpdateData(
                table: "rule_nse_scripts",
                keyColumns: new[] { "NseScriptId", "RuleId" },
                keyValues: new object[] { "mysql-info", "mysql_service_detected" },
                column: "ScanCheckId",
                value: "mysql-info");

            migrationBuilder.UpdateData(
                table: "rule_nse_scripts",
                keyColumns: new[] { "NseScriptId", "RuleId" },
                keyValues: new object[] { "banner", "pg_service_detected" },
                column: "ScanCheckId",
                value: "banner");

            migrationBuilder.CreateIndex(
                name: "IX_scan_script_results_ScanCheckId",
                table: "scan_script_results",
                column: "ScanCheckId");

            migrationBuilder.CreateIndex(
                name: "IX_rule_nse_scripts_ScanCheckId",
                table: "rule_nse_scripts",
                column: "ScanCheckId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_scan_script_results_ScanCheckId",
                table: "scan_script_results");

            migrationBuilder.DropIndex(
                name: "IX_rule_nse_scripts_ScanCheckId",
                table: "rule_nse_scripts");

            migrationBuilder.DropColumn(
                name: "ScanCheckId",
                table: "scan_script_results");

            migrationBuilder.DropColumn(
                name: "ScanCheckId",
                table: "rule_nse_scripts");
        }
    }
}
