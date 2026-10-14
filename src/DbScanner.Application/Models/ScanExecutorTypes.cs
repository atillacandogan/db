namespace DbScanner.Application.Models;

/// <summary>
/// Stable, server-controlled identifiers for scan executor implementations.
/// </summary>
public static class ScanExecutorTypes
{
    public const string NmapNse = "nmap-nse";
    public const string MsSqlNative = "mssql-native";
    public const string OracleNative = "oracle-native";
    public const string MySqlNative = "mysql-native";
    public const string PythonAgent = "python-agent";
    public const string ExternalApi = "external-api";

    /// <summary>
    /// Validates the identifier shape only. Executability is decided by the
    /// DI-backed strategy registry, so adding a strategy does not require
    /// changing this contract.
    /// </summary>
    public static bool IsValid(string? executorType)
    {
        if (string.IsNullOrWhiteSpace(executorType) || executorType.Length > 64 || executorType[0] == '-')
        {
            return false;
        }

        return executorType.All(character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    }
}
