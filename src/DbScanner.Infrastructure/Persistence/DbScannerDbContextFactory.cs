using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DbScanner.Infrastructure.Persistence;

public sealed class DbScannerDbContextFactory : IDesignTimeDbContextFactory<DbScannerDbContext>
{
    public DbScannerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DBSCANNER_MIGRATION_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=dbscanner;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<DbScannerDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new DbScannerDbContext(options);
    }
}
