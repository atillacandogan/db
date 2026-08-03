using System.Data.Common;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Enums;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using System.Text.RegularExpressions;

namespace DbScanner.Infrastructure.Targets;

public sealed class TargetConnectionStringParser : ITargetConnectionStringParser
{
    public ParsedTargetConnection Parse(string connectionString, DatabaseEngine engine)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string is required.");
        }

        if (connectionString.Length > 4096)
        {
            throw new InvalidOperationException("Connection string is too long.");
        }

        if (engine is not DatabaseEngine.PostgreSql
            and not DatabaseEngine.MySql
            and not DatabaseEngine.MsSql
            and not DatabaseEngine.Oracle)
        {
            throw new InvalidOperationException("Unsupported database engine.");
        }

        ParsedTargetConnection parsedConnection;

        try
        {
            parsedConnection = engine switch
            {
                DatabaseEngine.PostgreSql => ParsePostgreSql(connectionString),
                DatabaseEngine.MySql => ParseMySql(connectionString),
                DatabaseEngine.MsSql => ParseMsSql(connectionString),
                DatabaseEngine.Oracle => ParseOracle(connectionString),
                _ => throw new InvalidOperationException()
            };
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or FormatException
                or OverflowException
                or InvalidOperationException
                or KeyNotFoundException)
        {
            throw new InvalidOperationException(
                "Connection string could not be parsed for the selected engine.");
        }

        if (string.IsNullOrWhiteSpace(parsedConnection.Host))
        {
            throw new InvalidOperationException(
                engine is DatabaseEngine.MySql or DatabaseEngine.MsSql
                    ? "Connection string must include a server."
                    : "Connection string must include a host.");
        }

        return parsedConnection;
    }

    private static ParsedTargetConnection ParsePostgreSql(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        return new ParsedTargetConnection(
            builder.Host ?? string.Empty,
            builder.Port,
            NullIfWhiteSpace(builder.Database),
            NullIfWhiteSpace(builder.Username));
    }

    private static ParsedTargetConnection ParseMySql(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);

        return new ParsedTargetConnection(
            builder.Server,
            checked((int)builder.Port),
            NullIfWhiteSpace(builder.Database),
            NullIfWhiteSpace(builder.UserID));
    }

    private static ParsedTargetConnection ParseMsSql(string connectionString)
    {
        var genericBuilder = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString
        };

        int? explicitPort = null;
        if (genericBuilder.TryGetValue("Port", out var portValue))
        {
            explicitPort = ParsePort(portValue);
            genericBuilder.Remove("Port");
        }

        var builder = new SqlConnectionStringBuilder(genericBuilder.ConnectionString);
        var (host, dataSourcePort) = ParseMsSqlDataSource(builder.DataSource);
        var port = explicitPort ?? dataSourcePort ?? 1433;

        if (port is <= 0 or > 65535)
        {
            throw new FormatException("Microsoft SQL Server port is outside the valid range.");
        }

        return new ParsedTargetConnection(
            host,
            port,
            NullIfWhiteSpace(builder.InitialCatalog),
            NullIfWhiteSpace(builder.UserID));
    }

    private static ParsedTargetConnection ParseOracle(string connectionString)
    {
        var builder = new OracleConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource?.Trim() ?? string.Empty;
        var easyConnect = Regex.Match(
            dataSource,
            @"^(?://)?(?<host>\[[^\]]+\]|[^:/\s]+)(?::(?<port>\d{1,5}))?/(?<service>[^/?\s]+)",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(150));
        if (easyConnect.Success)
        {
            var host = easyConnect.Groups["host"].Value.Trim('[', ']');
            var port = easyConnect.Groups["port"].Success
                ? int.Parse(easyConnect.Groups["port"].Value, System.Globalization.CultureInfo.InvariantCulture)
                : 1521;
            ValidateOraclePort(port);
            return new ParsedTargetConnection(
                host,
                port,
                NullIfWhiteSpace(easyConnect.Groups["service"].Value),
                NullIfWhiteSpace(builder.UserID));
        }

        var hostMatch = Regex.Match(
            dataSource,
            @"\(HOST\s*=\s*(?<host>[^)]+)\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(150));
        var portMatch = Regex.Match(
            dataSource,
            @"\(PORT\s*=\s*(?<port>\d{1,5})\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(150));
        var serviceMatch = Regex.Match(
            dataSource,
            @"\((?:SERVICE_NAME|SID)\s*=\s*(?<service>[^)]+)\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(150));
        if (!hostMatch.Success)
        {
            throw new FormatException("Oracle data source must use Easy Connect or a connect descriptor.");
        }

        var descriptorPort = portMatch.Success
            ? int.Parse(portMatch.Groups["port"].Value, System.Globalization.CultureInfo.InvariantCulture)
            : 1521;
        ValidateOraclePort(descriptorPort);
        return new ParsedTargetConnection(
            hostMatch.Groups["host"].Value.Trim(),
            descriptorPort,
            NullIfWhiteSpace(serviceMatch.Groups["service"].Value),
            NullIfWhiteSpace(builder.UserID));
    }

    private static void ValidateOraclePort(int port)
    {
        if (port is <= 0 or > 65535)
        {
            throw new FormatException("Oracle Database port is outside the valid range.");
        }
    }

    private static (string Host, int? Port) ParseMsSqlDataSource(string dataSource)
    {
        var normalized = dataSource.Trim();
        if (normalized.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[4..].Trim();
        }

        var separatorIndex = normalized.LastIndexOf(',');
        if (separatorIndex <= 0 || separatorIndex == normalized.Length - 1)
        {
            return (normalized, null);
        }

        var port = ParsePort(normalized[(separatorIndex + 1)..]);
        return (normalized[..separatorIndex].Trim(), port);
    }

    private static int ParsePort(object value)
    {
        if (!int.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out var port))
        {
            throw new FormatException("Microsoft SQL Server port is invalid.");
        }

        return port;
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
