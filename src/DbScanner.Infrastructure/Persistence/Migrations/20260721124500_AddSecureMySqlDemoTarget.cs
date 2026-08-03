using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbScanner.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(DbScannerDbContext))]
    [Migration("20260721124500_AddSecureMySqlDemoTarget")]
    public partial class AddSecureMySqlDemoTarget : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO targets
                    ("Id", "Name", "Host", "Port", "Engine", "EnvironmentTag", "Enabled", "CreatedAt")
                VALUES
                    ('c5a34552-b74c-44e2-8d5d-2a2e37f8dc50',
                     'Secure MySQL 8.4',
                     'demo-mysql-secure-target',
                     3306,
                     'MySql',
                     'demo-secure',
                     TRUE,
                     TIMESTAMPTZ '2026-07-21 09:45:00+00')
                ON CONFLICT ("Host", "Port", "Engine") DO NOTHING;

                INSERT INTO scan_scope_allowlist
                    ("Id", "ScopeValue", "ScopeType", "Port", "Description", "Enabled", "CreatedAt")
                VALUES
                    ('a9f673d4-f878-4c58-8f0c-56e421d36d06',
                     'demo-mysql-secure-target',
                     'DemoTarget',
                     3306,
                     'Aspire secure demo MySQL 8.4 target',
                     TRUE,
                     TIMESTAMPTZ '2026-07-21 09:45:00+00')
                ON CONFLICT ("ScopeValue", "Port") DO NOTHING;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM scan_scope_allowlist
                WHERE "Id" = 'a9f673d4-f878-4c58-8f0c-56e421d36d06';

                DELETE FROM targets
                WHERE "Id" = 'c5a34552-b74c-44e2-8d5d-2a2e37f8dc50';
                """);
        }
    }
}
