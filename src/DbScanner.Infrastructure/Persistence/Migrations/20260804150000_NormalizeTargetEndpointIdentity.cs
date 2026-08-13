using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbScanner.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DbScannerDbContext))]
[Migration("20260804150000_NormalizeTargetEndpointIdentity")]
public partial class NormalizeTargetEndpointIdentity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "NormalizedHost",
            table: "targets",
            type: "character varying(255)",
            maxLength: 255,
            nullable: false,
            defaultValue: "");

        migrationBuilder.Sql(
            """
            UPDATE targets
            SET "NormalizedHost" = lower(trim(trailing '.' from btrim("Host")));
            """);

        migrationBuilder.DropIndex(
            name: "IX_targets_Host_Port_Engine",
            table: "targets");

        migrationBuilder.CreateIndex(
            name: "IX_targets_NormalizedHost_Port_Engine",
            table: "targets",
            columns: new[] { "NormalizedHost", "Port", "Engine" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_targets_NormalizedHost_Port_Engine",
            table: "targets");

        migrationBuilder.DropColumn(
            name: "NormalizedHost",
            table: "targets");

        migrationBuilder.CreateIndex(
            name: "IX_targets_Host_Port_Engine",
            table: "targets",
            columns: new[] { "Host", "Port", "Engine" },
            unique: true);
    }
}
