using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

internal sealed class MsSqlReadinessHealthCheck(
    IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var password = configuration["Parameters:demo-mssql-password"];
        if (string.IsNullOrWhiteSpace(password))
        {
            return HealthCheckResult.Unhealthy("Microsoft SQL Server readiness secret is not configured.");
        }

        var port = int.TryParse(
            configuration["Parameters:demo-mssql-host-port"],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var configuredPort)
            ? configuredPort
            : 11433;
        var connectionString = new SqlConnectionStringBuilder
        {
            DataSource = $"127.0.0.1,{port}",
            InitialCatalog = "master",
            UserID = "sa",
            Password = password,
            Encrypt = true,
            TrustServerCertificate = true,
            ConnectTimeout = 2,
            Pooling = false,
            PersistSecurityInfo = false,
            ApplicationName = "DbScanner.AppHost.Readiness"
        }.ConnectionString;

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = 2;
            _ = await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy("SQL Server accepts queries.");
        }
        catch (Exception exception) when (
            exception is SqlException
                or TimeoutException
                or InvalidOperationException)
        {
            return HealthCheckResult.Unhealthy("SQL Server is not ready to accept queries.");
        }
    }
}
