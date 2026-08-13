using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbScanner.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DbScannerDbContext))]
[Migration("20260806120000_AddDiscoveryAllowlistManagement")]
public partial class AddDiscoveryAllowlistManagement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Name",
            table: "scan_scope_allowlist",
            type: "character varying(160)",
            maxLength: 160,
            nullable: false,
            defaultValue: "");
        migrationBuilder.AddColumn<string>(
            name: "EnvironmentTag",
            table: "scan_scope_allowlist",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "demo");
        migrationBuilder.AddColumn<string>(
            name: "AllowedEngines",
            table: "scan_scope_allowlist",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false,
            defaultValue: "mysql,postgresql,mssql,oracle");
        migrationBuilder.AddColumn<string>(
            name: "AllowedPorts",
            table: "scan_scope_allowlist",
            type: "character varying(1024)",
            maxLength: 1024,
            nullable: false,
            defaultValue: "");
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "UpdatedAt",
            table: "scan_scope_allowlist",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "CURRENT_TIMESTAMP");

        migrationBuilder.Sql(
            """
            UPDATE scan_scope_allowlist
            SET "Name" = COALESCE(NULLIF("Description", ''), "ScopeValue"),
                "EnvironmentTag" = 'demo',
                "AllowedEngines" = CASE "Port"
                    WHEN 3306 THEN 'mysql'
                    WHEN 5432 THEN 'postgresql'
                    WHEN 1433 THEN 'mssql'
                    WHEN 1521 THEN 'oracle'
                    ELSE 'mysql,postgresql,mssql,oracle'
                END,
                "AllowedPorts" = CASE "Port"
                    WHEN 3306 THEN 'mysql:3306'
                    WHEN 5432 THEN 'postgresql:5432'
                    WHEN 1433 THEN 'mssql:1433'
                    WHEN 1521 THEN 'oracle:1521'
                    WHEN 6379 THEN 'mysql:6379;postgresql:6379;mssql:6379;oracle:6379'
                    WHEN 27017 THEN 'mysql:27017;postgresql:27017;mssql:27017;oracle:27017'
                    ELSE ''
                END,
                "UpdatedAt" = "CreatedAt";
            """);

        migrationBuilder.DropIndex(
            name: "IX_scan_scope_allowlist_ScopeValue_Port",
            table: "scan_scope_allowlist");
        migrationBuilder.CreateIndex(
            name: "IX_scan_scope_allowlist_EnvironmentTag_ScopeType_ScopeValue",
            table: "scan_scope_allowlist",
            columns: new[] { "EnvironmentTag", "ScopeType", "ScopeValue" },
            unique: true);

        migrationBuilder.AddColumn<string>(
            name: "NormalizedHost",
            table: "discovered_databases",
            type: "character varying(255)",
            maxLength: 255,
            nullable: false,
            defaultValue: "");
        migrationBuilder.AddColumn<string>(
            name: "EnvironmentTag",
            table: "discovered_databases",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "demo");
        migrationBuilder.AddColumn<string>(
            name: "FingerprintLevel",
            table: "discovered_databases",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Unknown");

        migrationBuilder.Sql(
            """
            UPDATE discovered_databases AS result
            SET "NormalizedHost" = lower(trim(trailing '.' from btrim(result."Host"))),
                "EnvironmentTag" = job."EnvironmentTag",
                "FingerprintLevel" = CASE
                    WHEN result."Confidence" >= 90 THEN 'Confirmed'
                    WHEN result."Confidence" >= 60 THEN 'PortOnly'
                    ELSE 'Unknown'
                END
            FROM discovery_jobs AS job
            WHERE job."Id" = result."DiscoveryJobId";
            """);

        migrationBuilder.DropIndex(
            name: "IX_discovered_databases_DiscoveryJobId_Host_Port",
            table: "discovered_databases");

        // The previous index allowed the same endpoint to be stored once per discovery job.
        // Consolidate those historical rows before enforcing endpoint-level uniqueness. Prefer
        // a row already linked to a Target, then the newest/highest-confidence observation.
        migrationBuilder.Sql(
            """
            WITH ranked_results AS (
                SELECT
                    "Id",
                    ROW_NUMBER() OVER (
                        PARTITION BY "NormalizedHost", "Port", "DetectedEngine", "EnvironmentTag"
                        ORDER BY
                            ("TargetId" IS NOT NULL) DESC,
                            "CreatedAt" DESC,
                            "Confidence" DESC,
                            "Id" DESC
                    ) AS duplicate_rank
                FROM discovered_databases
            )
            DELETE FROM discovered_databases AS result_to_remove
            USING ranked_results AS ranked
            WHERE result_to_remove."Id" = ranked."Id"
              AND ranked.duplicate_rank > 1;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_discovered_databases_NormalizedHost_Port_DetectedEngine_EnvironmentTag",
            table: "discovered_databases",
            columns: new[] { "NormalizedHost", "Port", "DetectedEngine", "EnvironmentTag" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_discovered_databases_NormalizedHost_Port_DetectedEngine_EnvironmentTag",
            table: "discovered_databases");
        migrationBuilder.DropColumn(name: "NormalizedHost", table: "discovered_databases");
        migrationBuilder.DropColumn(name: "EnvironmentTag", table: "discovered_databases");
        migrationBuilder.DropColumn(name: "FingerprintLevel", table: "discovered_databases");
        migrationBuilder.CreateIndex(
            name: "IX_discovered_databases_DiscoveryJobId_Host_Port",
            table: "discovered_databases",
            columns: new[] { "DiscoveryJobId", "Host", "Port" },
            unique: true);

        migrationBuilder.DropIndex(
            name: "IX_scan_scope_allowlist_EnvironmentTag_ScopeType_ScopeValue",
            table: "scan_scope_allowlist");
        migrationBuilder.DropColumn(name: "Name", table: "scan_scope_allowlist");
        migrationBuilder.DropColumn(name: "EnvironmentTag", table: "scan_scope_allowlist");
        migrationBuilder.DropColumn(name: "AllowedEngines", table: "scan_scope_allowlist");
        migrationBuilder.DropColumn(name: "AllowedPorts", table: "scan_scope_allowlist");
        migrationBuilder.DropColumn(name: "UpdatedAt", table: "scan_scope_allowlist");
        migrationBuilder.CreateIndex(
            name: "IX_scan_scope_allowlist_ScopeValue_Port",
            table: "scan_scope_allowlist",
            columns: new[] { "ScopeValue", "Port" },
            unique: true);
    }
}
