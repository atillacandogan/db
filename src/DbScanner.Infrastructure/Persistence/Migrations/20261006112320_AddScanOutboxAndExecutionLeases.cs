using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScanOutboxAndExecutionLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExecutionAttempt",
                table: "scans",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastHeartbeatAt",
                table: "scans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                table: "scans",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseToken",
                table: "scans",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "scan_outbox_messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    DeadLetter = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DispatchToken = table.Column<Guid>(type: "uuid", nullable: true),
                    DispatchExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveryAttempts = table.Column<int>(type: "integer", nullable: false),
                    LastErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_outbox_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_scan_outbox_messages_scans_ScanId",
                        column: x => x.ScanId,
                        principalTable: "scans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_scans_Status_LeaseExpiresAt",
                table: "scans",
                columns: new[] { "Status", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_scan_outbox_messages_PublishedAt_AvailableAt",
                table: "scan_outbox_messages",
                columns: new[] { "PublishedAt", "AvailableAt" });

            migrationBuilder.CreateIndex(
                name: "IX_scan_outbox_messages_ScanId",
                table: "scan_outbox_messages",
                column: "ScanId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scan_outbox_messages");

            migrationBuilder.DropIndex(
                name: "IX_scans_Status_LeaseExpiresAt",
                table: "scans");

            migrationBuilder.DropColumn(
                name: "ExecutionAttempt",
                table: "scans");

            migrationBuilder.DropColumn(
                name: "LastHeartbeatAt",
                table: "scans");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "scans");

            migrationBuilder.DropColumn(
                name: "LeaseToken",
                table: "scans");
        }
    }
}
