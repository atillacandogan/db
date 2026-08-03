using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DbScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "nse_scripts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NseScriptName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SupportedEngines = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Categories = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DefaultEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    RequiresCredentials = table.Column<bool>(type: "boolean", nullable: false),
                    IsIntrusive = table.Column<bool>(type: "boolean", nullable: false),
                    AllowedArgsSchema = table.Column<string>(type: "jsonb", nullable: true),
                    ParserType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MatcherType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MatcherExpression = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Weight = table.Column<int>(type: "integer", nullable: false),
                    RemediationTemplate = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    DocUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ApprovalStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nse_scripts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Description = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Engine = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Weight = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    ParserType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MatcherType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MatcherExpression = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Remediation = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "scan_scope_allowlist",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeValue = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: true),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_scope_allowlist", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "targets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Engine = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EnvironmentTag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_targets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "nse_script_matchers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NseScriptId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RuleId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ParserType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MatcherType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MatcherExpression = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Engine = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_nse_script_matchers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_nse_script_matchers_nse_scripts_NseScriptId",
                        column: x => x.NseScriptId,
                        principalTable: "nse_scripts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_nse_script_matchers_rules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "rule_engine_support",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Engine = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UnsupportedStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rule_engine_support", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rule_engine_support_rules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rule_nse_scripts",
                columns: table => new
                {
                    RuleId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NseScriptId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rule_nse_scripts", x => new { x.RuleId, x.NseScriptId });
                    table.ForeignKey(
                        name: "FK_rule_nse_scripts_nse_scripts_NseScriptId",
                        column: x => x.NseScriptId,
                        principalTable: "nse_scripts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_rule_nse_scripts_rules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RiskScore = table.Column<int>(type: "integer", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TimeoutAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_scans_targets_TargetId",
                        column: x => x.TargetId,
                        principalTable: "targets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "findings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ScoreContribution = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Evidence = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    Remediation = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SourceScript = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DetectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_findings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_findings_rules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "rules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_findings_scans_ScanId",
                        column: x => x.ScanId,
                        principalTable: "scans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_findings_targets_TargetId",
                        column: x => x.TargetId,
                        principalTable: "targets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "scan_script_results",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScanId = table.Column<Guid>(type: "uuid", nullable: false),
                    NseScriptId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ScriptName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RawXml = table.Column<string>(type: "text", nullable: true),
                    NormalizedOutput = table.Column<string>(type: "text", nullable: true),
                    ExitCode = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scan_script_results", x => x.Id);
                    table.ForeignKey(
                        name: "FK_scan_script_results_nse_scripts_NseScriptId",
                        column: x => x.NseScriptId,
                        principalTable: "nse_scripts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_scan_script_results_scans_ScanId",
                        column: x => x.ScanId,
                        principalTable: "scans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "nse_scripts",
                columns: new[] { "Id", "AllowedArgsSchema", "ApprovalStatus", "Categories", "DefaultEnabled", "Description", "DisplayName", "DocUrl", "IsIntrusive", "MatcherExpression", "MatcherType", "NseScriptName", "ParserType", "RemediationTemplate", "RequiresCredentials", "Severity", "SupportedEngines", "Weight" },
                values: new object[,]
                {
                    { "banner", null, "Approved", "safe,discovery", true, "Generic service banner collection.", "Banner", null, false, null, "Contains", "banner", "GenericOutput", null, false, "Info", "generic,postgresql,mysql", 0 },
                    { "mysql-info", null, "Approved", "safe,discovery", true, "Collects safe unauthenticated MySQL service metadata where available.", "MySQL Info", null, false, null, "Contains", "mysql-info", "StructuredXml", null, false, "Info", "mysql", 0 }
                });

            migrationBuilder.InsertData(
                table: "rules",
                columns: new[] { "Id", "Description", "Enabled", "Engine", "MatcherExpression", "MatcherType", "ParserType", "Remediation", "Severity", "Title", "Weight" },
                values: new object[,]
                {
                    { "generic_db_port_open", "Database service port is reachable.", true, "Generic", "open", "Contains", "StructuredXml", "Confirm the database service is expected to be reachable from the scanner network.", "Info", "DB port open", 0 },
                    { "generic_service_fingerprint_detected", "Nmap service fingerprint returned database metadata.", true, "Generic", "service", "Contains", "StructuredXml", "Reduce unnecessary service metadata exposure where possible.", "Low", "Service fingerprint detected", 1 },
                    { "mysql_service_detected", "MySQL service is detected on the target.", true, "MySql", "mysql", "Contains", "StructuredXml", "Confirm the MySQL exposure is expected.", "Info", "MySQL service detected", 0 },
                    { "pg_service_detected", "PostgreSQL service is detected on the target.", true, "PostgreSql", "postgresql", "Contains", "StructuredXml", "Confirm the PostgreSQL exposure is expected.", "Info", "PostgreSQL service detected", 0 }
                });

            migrationBuilder.InsertData(
                table: "scan_scope_allowlist",
                columns: new[] { "Id", "CreatedAt", "Description", "Enabled", "Port", "ScopeType", "ScopeValue" },
                values: new object[,]
                {
                    { new Guid("25585dc7-f312-44ba-b01d-ef31cc312d10"), new DateTimeOffset(new DateTime(2026, 4, 13, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Aspire demo PostgreSQL target", true, 5432, "DemoTarget", "demo-postgres-target" },
                    { new Guid("aa02f7f9-7a0e-4609-9e85-53b1bda8b1e3"), new DateTimeOffset(new DateTime(2026, 4, 13, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Aspire demo MySQL target", true, 3306, "DemoTarget", "demo-mysql-target" }
                });

            migrationBuilder.InsertData(
                table: "targets",
                columns: new[] { "Id", "CreatedAt", "Enabled", "Engine", "EnvironmentTag", "Host", "Name", "Port" },
                values: new object[,]
                {
                    { new Guid("3d79cfbe-dbb5-4d95-b476-854ed970d43d"), new DateTimeOffset(new DateTime(2026, 4, 13, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "PostgreSql", "demo", "demo-postgres-target", "Demo PostgreSQL", 5432 },
                    { new Guid("b2fd6c5a-98b4-4641-8e98-4b95c5ba98db"), new DateTimeOffset(new DateTime(2026, 4, 13, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "MySql", "demo", "demo-mysql-target", "Demo MySQL", 3306 }
                });

            migrationBuilder.InsertData(
                table: "nse_script_matchers",
                columns: new[] { "Id", "Enabled", "Engine", "MatcherExpression", "MatcherType", "NseScriptId", "ParserType", "RuleId" },
                values: new object[,]
                {
                    { new Guid("46971bbd-f10f-4d30-8d0a-8d48a273854b"), true, "PostgreSql", "postgresql", "Contains", "banner", "GenericOutput", "pg_service_detected" },
                    { new Guid("67a5a68b-f8d1-4234-b430-8c96e1b16868"), true, "MySql", "mysql", "Contains", "mysql-info", "GenericOutput", "mysql_service_detected" },
                    { new Guid("b09ad4e0-e2f9-4ea4-aea0-6bf5d433d4d7"), true, "Generic", "open", "Contains", "banner", "GenericOutput", "generic_db_port_open" }
                });

            migrationBuilder.InsertData(
                table: "rule_engine_support",
                columns: new[] { "Id", "Engine", "RuleId", "UnsupportedStatus" },
                values: new object[,]
                {
                    { new Guid("1ef15d1d-1b8e-485f-a852-3cfc7d4bdc5d"), "PostgreSql", "generic_db_port_open", "NotSupported" },
                    { new Guid("3642e3b4-ea54-4a91-9731-ded6e7ee17c7"), "MySql", "mysql_service_detected", "NotSupported" },
                    { new Guid("5d232cfa-7a8f-40c8-926d-1056ce698b47"), "MySql", "generic_db_port_open", "NotSupported" },
                    { new Guid("6477a0ab-d8c6-474e-b2d2-554e4d7e9e28"), "PostgreSql", "pg_service_detected", "NotSupported" },
                    { new Guid("8c4e5d35-9b8e-44e7-bbcb-c80b0e7d14c5"), "MySql", "generic_service_fingerprint_detected", "NotSupported" },
                    { new Guid("fd89de34-36ee-42ac-a16d-27a97760bc97"), "PostgreSql", "generic_service_fingerprint_detected", "NotSupported" }
                });

            migrationBuilder.InsertData(
                table: "rule_nse_scripts",
                columns: new[] { "NseScriptId", "RuleId" },
                values: new object[,]
                {
                    { "banner", "generic_db_port_open" },
                    { "banner", "generic_service_fingerprint_detected" },
                    { "mysql-info", "mysql_service_detected" },
                    { "banner", "pg_service_detected" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_findings_RuleId",
                table: "findings",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_findings_ScanId",
                table: "findings",
                column: "ScanId");

            migrationBuilder.CreateIndex(
                name: "IX_findings_TargetId",
                table: "findings",
                column: "TargetId");

            migrationBuilder.CreateIndex(
                name: "IX_nse_script_matchers_NseScriptId",
                table: "nse_script_matchers",
                column: "NseScriptId");

            migrationBuilder.CreateIndex(
                name: "IX_nse_script_matchers_RuleId",
                table: "nse_script_matchers",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_nse_scripts_NseScriptName",
                table: "nse_scripts",
                column: "NseScriptName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rule_engine_support_RuleId_Engine",
                table: "rule_engine_support",
                columns: new[] { "RuleId", "Engine" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rule_nse_scripts_NseScriptId",
                table: "rule_nse_scripts",
                column: "NseScriptId");

            migrationBuilder.CreateIndex(
                name: "IX_scan_scope_allowlist_ScopeValue_Port",
                table: "scan_scope_allowlist",
                columns: new[] { "ScopeValue", "Port" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_scan_script_results_NseScriptId",
                table: "scan_script_results",
                column: "NseScriptId");

            migrationBuilder.CreateIndex(
                name: "IX_scan_script_results_ScanId",
                table: "scan_script_results",
                column: "ScanId");

            migrationBuilder.CreateIndex(
                name: "IX_scans_TargetId_Status",
                table: "scans",
                columns: new[] { "TargetId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_targets_Host_Port_Engine",
                table: "targets",
                columns: new[] { "Host", "Port", "Engine" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "findings");

            migrationBuilder.DropTable(
                name: "nse_script_matchers");

            migrationBuilder.DropTable(
                name: "rule_engine_support");

            migrationBuilder.DropTable(
                name: "rule_nse_scripts");

            migrationBuilder.DropTable(
                name: "scan_scope_allowlist");

            migrationBuilder.DropTable(
                name: "scan_script_results");

            migrationBuilder.DropTable(
                name: "rules");

            migrationBuilder.DropTable(
                name: "nse_scripts");

            migrationBuilder.DropTable(
                name: "scans");

            migrationBuilder.DropTable(
                name: "targets");
        }
    }
}
