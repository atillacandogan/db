using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Oracle.ManagedDataAccess.Client;

internal sealed class OracleReadinessHealthCheck(
    IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var password = configuration["Parameters:demo-oracle-scanner-password"];
        if (string.IsNullOrWhiteSpace(password))
        {
            return HealthCheckResult.Unhealthy("Oracle Database readiness secret is not configured.");
        }

        var port = int.TryParse(
            configuration["Parameters:demo-oracle-host-port"],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var configuredPort)
            ? configuredPort
            : 11521;
        var connectionString = new OracleConnectionStringBuilder
        {
            DataSource = $"//127.0.0.1:{port}/FREEPDB1",
            UserID = "dbscanner_scanner",
            Password = password,
            Pooling = false,
            PersistSecurityInfo = false,
            ConnectionTimeout = 2
        }.ConnectionString;

        try
        {
            await using var connection = new OracleConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM DUAL";
            command.CommandTimeout = 2;
            _ = await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("Oracle Database accepts queries.");
        }
        catch (Exception exception) when (
            exception is OracleException
                or TimeoutException
                or InvalidOperationException)
        {
            return HealthCheckResult.Unhealthy("Oracle Database is not ready to accept queries.");
        }
    }
}
