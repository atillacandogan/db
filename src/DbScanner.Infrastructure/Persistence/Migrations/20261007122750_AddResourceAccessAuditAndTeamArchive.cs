using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceAccessAuditAndTeamArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "access_teams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "resource_access_audits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorSubject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ActorUsername = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    BeforeJson = table.Column<string>(type: "jsonb", nullable: true),
                    AfterJson = table.Column<string>(type: "jsonb", nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resource_access_audits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_resource_access_audits_TeamId_Timestamp",
                table: "resource_access_audits",
                columns: new[] { "TeamId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_resource_access_audits_Timestamp_Id",
                table: "resource_access_audits",
                columns: new[] { "Timestamp", "Id" });

            migrationBuilder.Sql("""
                CREATE FUNCTION dbscanner_reject_access_audit_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Authorization audit records are append-only.' USING ERRCODE = '55000';
                END;
                $$;
                CREATE TRIGGER access_audit_no_update_delete
                    BEFORE UPDATE OR DELETE ON resource_access_audits
                    FOR EACH ROW EXECUTE FUNCTION dbscanner_reject_access_audit_mutation();
                CREATE TRIGGER access_audit_no_truncate
                    BEFORE TRUNCATE ON resource_access_audits
                    FOR EACH STATEMENT EXECUTE FUNCTION dbscanner_reject_access_audit_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "resource_access_audits");
            migrationBuilder.Sql("DROP FUNCTION dbscanner_reject_access_audit_mutation();");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "access_teams");
        }
    }
}
