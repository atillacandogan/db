using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DbScanner.Infrastructure.Persistence;

public sealed class DbScannerDbContextFactory : IDesignTimeDbContextFactory<DbScannerDbContext>
{
    public DbScannerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("DBSCANNER_MIGRATION_CONNECTION")
            // Model-only migration commands need no database credentials. Database
            // updates must supply DBSCANNER_MIGRATION_CONNECTION explicitly.
            ?? "Host=localhost;Port=5432;Database=dbscanner;Username=postgres";

        var options = new DbContextOptionsBuilder<DbScannerDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new DbScannerDbContext(options, DbScanner.Application.Models.ResourceAccessContext.ForSystem());
    }
}
