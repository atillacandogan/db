using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DbScanner.Infrastructure.Persistence;

public static class MigrationExtensions
{
    private const int MaxMigrationAttempts = 10;

    public static async Task ApplyDbScannerMigrationsAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var logger = services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("DbScanner.DatabaseMigrations");

        for (var attempt = 1; attempt <= MaxMigrationAttempts; attempt++)
        {
            try
            {
                using var scope = services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DbScannerDbContext>();
                await dbContext.Database.MigrateAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (
                attempt < MaxMigrationAttempts
                && exception is NpgsqlException or TimeoutException)
            {
                var retryDelay = TimeSpan.FromSeconds(Math.Min(attempt * 2, 10));
                logger.LogWarning(
                    exception,
                    "Database is not ready for migrations. Retrying in {RetryDelaySeconds} seconds ({Attempt}/{MaxAttempts}).",
                    retryDelay.TotalSeconds,
                    attempt,
                    MaxMigrationAttempts);
                await Task.Delay(retryDelay, cancellationToken);
            }
        }
    }
}
