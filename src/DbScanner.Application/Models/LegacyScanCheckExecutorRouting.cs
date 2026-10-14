using DbScanner.Domain.Entities;
using DbScanner.Application.Observability;

namespace DbScanner.Application.Models;

/// <summary>
/// Single compatibility rule for catalog rows created before ExecutorType was
/// persisted. New records must carry an explicit executor identifier.
/// </summary>
public static class LegacyScanCheckExecutorRouting
{
    public static string Resolve(NseScript script)
    {
        ArgumentNullException.ThrowIfNull(script);

        if (!string.IsNullOrWhiteSpace(script.ExecutorType))
        {
            var explicitType = script.ExecutorType.Trim();
            if (!ScanExecutorTypes.IsValid(explicitType))
            {
                throw new InvalidOperationException(
                    $"Catalog check '{script.Id}' has invalid executor type '{script.ExecutorType}'.");
            }

            return explicitType;
        }

        ScanCompatibilityTelemetry.RecordFallback("executor-routing");

        return script.NseScriptName switch
        {
            var name when string.Equals(
                name,
                "mysql-empty-password",
                StringComparison.OrdinalIgnoreCase) => ScanExecutorTypes.MySqlNative,
            var name when string.Equals(
                name,
                "mssql-browser-discovery",
                StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("mssql-auth-", StringComparison.OrdinalIgnoreCase) =>
                ScanExecutorTypes.MsSqlNative,
            var name when name.StartsWith("oracle-auth-", StringComparison.OrdinalIgnoreCase) =>
                ScanExecutorTypes.OracleNative,
            _ => ScanExecutorTypes.NmapNse
        };
    }
}
