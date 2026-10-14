using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReconcileDiscoveryModelSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_discovered_databases_DiscoveryJobId",
                table: "discovered_databases",
                column: "DiscoveryJobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_discovered_databases_DiscoveryJobId",
                table: "discovered_databases");
        }
    }
}
