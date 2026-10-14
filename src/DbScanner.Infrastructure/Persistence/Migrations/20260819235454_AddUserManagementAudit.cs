using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserManagementAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_management_audits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorSubject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ActorUsername = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ActorEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetUserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PreviousRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    NewRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PreviousEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    NewEnabled = table.Column<bool>(type: "boolean", nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_management_audits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_management_audits_TargetUserId_Timestamp",
                table: "user_management_audits",
                columns: new[] { "TargetUserId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_user_management_audits_Timestamp",
                table: "user_management_audits",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_management_audits");
        }
    }
}
