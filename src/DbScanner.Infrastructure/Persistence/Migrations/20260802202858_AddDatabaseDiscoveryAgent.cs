using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDatabaseDiscoveryAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "discovery_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ScopeInput = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    EnvironmentTag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SupportedEngineFilter = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discovery_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "discovered_databases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscoveryJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    DetectedService = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DetectedEngine = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Confidence = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_discovered_databases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_discovered_databases_discovery_jobs_DiscoveryJobId",
                        column: x => x.DiscoveryJobId,
                        principalTable: "discovery_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_discovered_databases_targets_TargetId",
                        column: x => x.TargetId,
                        principalTable: "targets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_discovered_databases_DiscoveryJobId_Host_Port",
                table: "discovered_databases",
                columns: new[] { "DiscoveryJobId", "Host", "Port" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_discovered_databases_TargetId",
                table: "discovered_databases",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_discovery_jobs_CreatedAt",
                table: "discovery_jobs",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discovered_databases");

            migrationBuilder.DropTable(
                name: "discovery_jobs");
        }
    }
}
