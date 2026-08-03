using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbScanner.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(DbScannerDbContext))]
    [Migration("20260714120000_AddNseScriptInstalledState")]
    public partial class AddNseScriptInstalledState : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsInstalled",
                table: "nse_scripts",
                type: "boolean",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE nse_scripts SET \"IsInstalled\" = TRUE WHERE \"Source\" = 'Installed';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsInstalled",
                table: "nse_scripts");
        }
    }
}
