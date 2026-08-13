using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DbScanner.Infrastructure.Persistence.Migrations;

[DbContext(typeof(DbScannerDbContext))]
[Migration("20260804140500_AddUnsupportedRedisDiscoveryDemo")]
public partial class AddUnsupportedRedisDiscoveryDemo : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            INSERT INTO scan_scope_allowlist
              ("Id", "ScopeValue", "ScopeType", "Port", "Description", "Enabled", "CreatedAt")
            VALUES
              ('b6c57a71-b337-4ce6-b291-96fa88808180', 'demo-redis-unsupported-target',
               'DemoTarget', 6379, 'Aspire unsupported Redis discovery demo', TRUE,
               TIMESTAMPTZ '2026-08-04 14:05:00+00')
            ON CONFLICT ("ScopeValue", "Port") DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM scan_scope_allowlist
            WHERE "Id" = 'b6c57a71-b337-4ce6-b291-96fa88808180';
            """);
    }
}
