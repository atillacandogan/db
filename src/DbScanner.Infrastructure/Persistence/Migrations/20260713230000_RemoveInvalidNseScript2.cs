using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbScanner.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(DbScannerDbContext))]
    [Migration("20260713230000_RemoveInvalidNseScript2")]
    public partial class RemoveInvalidNseScript2 : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM nse_scripts
                WHERE "Id" = '2'
                  AND "NseScriptName" = '2'
                  AND "DisplayName" = '2'
                  AND "Description" = '2'
                  AND "Source" = 'Installed';
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
