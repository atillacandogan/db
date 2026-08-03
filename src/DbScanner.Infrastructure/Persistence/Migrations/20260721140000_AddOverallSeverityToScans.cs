using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbScanner.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(DbScannerDbContext))]
    [Migration("20260721140000_AddOverallSeverityToScans")]
    public partial class AddOverallSeverityToScans : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OverallSeverity",
                table: "scans",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE scans AS scan
                SET "RiskScore" = LEAST(
                    100,
                    COALESCE((
                        SELECT SUM(
                            CASE
                                WHEN finding."ScoreContribution" > 0
                                    THEN finding."ScoreContribution"
                                WHEN LOWER(finding."Severity") = 'critical' THEN 5
                                WHEN LOWER(finding."Severity") = 'high' THEN 3
                                WHEN LOWER(finding."Severity") = 'medium' THEN 2
                                WHEN LOWER(finding."Severity") = 'low' THEN 1
                                ELSE 0
                            END)
                        FROM findings AS finding
                        WHERE finding."ScanId" = scan."Id"
                    ), 0))
                WHERE scan."Status" = 'Scanned';

                WITH severity_assessment AS (
                    SELECT
                        scan."Id",
                        GREATEST(
                            CASE
                                WHEN scan."RiskScore" >= 76 THEN 4
                                WHEN scan."RiskScore" >= 51 THEN 3
                                WHEN scan."RiskScore" >= 26 THEN 2
                                WHEN scan."RiskScore" >= 1 THEN 1
                                ELSE 0
                            END,
                            COALESCE(MAX(
                                CASE
                                    WHEN LOWER(finding."Severity") = 'critical' THEN 4
                                    WHEN LOWER(finding."Severity") = 'high' THEN 3
                                    WHEN LOWER(finding."Severity") = 'medium' THEN 2
                                    WHEN LOWER(finding."Severity") = 'low' THEN 1
                                    ELSE 0
                                END), 0)
                        ) AS severity_rank
                    FROM scans AS scan
                    LEFT JOIN findings AS finding ON finding."ScanId" = scan."Id"
                    WHERE scan."Status" = 'Scanned'
                    GROUP BY scan."Id", scan."RiskScore"
                )
                UPDATE scans AS scan
                SET "OverallSeverity" = CASE assessment.severity_rank
                    WHEN 4 THEN 'Critical'
                    WHEN 3 THEN 'High'
                    WHEN 2 THEN 'Medium'
                    WHEN 1 THEN 'Low'
                    ELSE 'Info'
                END
                FROM severity_assessment AS assessment
                WHERE scan."Id" = assessment."Id";
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OverallSeverity",
                table: "scans");
        }
    }
}
