using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DbScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectAndTeamTargetAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "targets",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.AddUniqueConstraint(
                name: "AK_targets_Id_ProjectId",
                table: "targets",
                columns: new[] { "Id", "ProjectId" });

            migrationBuilder.CreateTable(
                name: "access_projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "access_teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_teams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "deployment_tenant",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deployment_tenant", x => x.Id);
                    table.CheckConstraint("CK_deployment_tenant_singleton", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "access_grants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    Permissions = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_grants", x => x.Id);
                    table.CheckConstraint("CK_access_grants_permissions", "\"Permissions\" BETWEEN 1 AND 15");
                    table.ForeignKey(
                        name: "FK_access_grants_access_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "access_projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_access_grants_access_teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "access_teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_access_grants_targets_TargetId_ProjectId",
                        columns: x => new { x.TargetId, x.ProjectId },
                        principalTable: "targets",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "access_team_members",
                columns: table => new
                {
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_team_members", x => new { x.TeamId, x.Subject });
                    table.ForeignKey(
                        name: "FK_access_team_members_access_teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "access_teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "access_projects",
                columns: new[] { "Id", "Name" },
                values: new object[] { new Guid("00000000-0000-0000-0000-000000000001"), "Varsayılan proje" });

            migrationBuilder.CreateIndex(
                name: "IX_targets_ProjectId",
                table: "targets",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_access_grants_ProjectId",
                table: "access_grants",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_access_grants_TargetId_ProjectId",
                table: "access_grants",
                columns: new[] { "TargetId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_access_grants_TeamId_ProjectId_TargetId",
                table: "access_grants",
                columns: new[] { "TeamId", "ProjectId", "TargetId" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_access_team_members_Subject",
                table: "access_team_members",
                column: "Subject");

            migrationBuilder.AddForeignKey(
                name: "FK_targets_access_projects_ProjectId",
                table: "targets",
                column: "ProjectId",
                principalTable: "access_projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_targets_access_projects_ProjectId",
                table: "targets");

            migrationBuilder.DropTable(
                name: "access_grants");

            migrationBuilder.DropTable(
                name: "access_team_members");

            migrationBuilder.DropTable(
                name: "deployment_tenant");

            migrationBuilder.DropTable(
                name: "access_projects");

            migrationBuilder.DropTable(
                name: "access_teams");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_targets_Id_ProjectId",
                table: "targets");

            migrationBuilder.DropIndex(
                name: "IX_targets_ProjectId",
                table: "targets");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "targets");
        }
    }
}
