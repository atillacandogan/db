using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScanCheckExecutorMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExecutorType",
                table: "nse_scripts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "nse_scripts",
                keyColumn: "Id",
                keyValue: "banner",
                column: "ExecutorType",
                value: "nmap-nse");

            migrationBuilder.UpdateData(
                table: "nse_scripts",
                keyColumn: "Id",
                keyValue: "mysql-info",
                column: "ExecutorType",
                value: "nmap-nse");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExecutorType",
                table: "nse_scripts");
        }
    }
}
