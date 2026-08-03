using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbScanner.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(DbScannerDbContext))]
    [Migration("20260721133000_AddRootEmptyMySqlDemoTarget")]
    public partial class AddRootEmptyMySqlDemoTarget : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO targets
                    ("Id", "Name", "Host", "Port", "Engine", "EnvironmentTag", "Enabled", "CreatedAt")
                VALUES
                    ('7d19b5af-8b6c-4d53-87f5-30a2306c240e',
                     'Root Empty Password MySQL 8.4',
                     'demo-mysql-root-empty-target',
                     3306,
                     'MySql',
                     'demo-root-empty',
                     TRUE,
                     TIMESTAMPTZ '2026-07-21 10:30:00+00')
                ON CONFLICT ("Host", "Port", "Engine") DO NOTHING;

                INSERT INTO scan_scope_allowlist
                    ("Id", "ScopeValue", "ScopeType", "Port", "Description", "Enabled", "CreatedAt")
                VALUES
                    ('e4ec108b-fce0-4fa0-87b6-47fd70b77129',
                     'demo-mysql-root-empty-target',
                     'DemoTarget',
                     3306,
                     'Aspire MySQL 8.4 target with a remotely accessible empty-password root account',
                     TRUE,
                     TIMESTAMPTZ '2026-07-21 10:30:00+00')
                ON CONFLICT ("ScopeValue", "Port") DO NOTHING;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM scan_scope_allowlist
                WHERE "Id" = 'e4ec108b-fce0-4fa0-87b6-47fd70b77129';

                DELETE FROM targets
                WHERE "Id" = '7d19b5af-8b6c-4d53-87f5-30a2306c240e';
                """);
        }
    }
}
