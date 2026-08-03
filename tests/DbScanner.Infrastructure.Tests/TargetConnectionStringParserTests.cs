using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Targets;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

public sealed class TargetConnectionStringParserTests
{
    private readonly TargetConnectionStringParser _parser = new();

    [Fact]
    public void Parse_ExtractsPostgreSqlSafeFields()
    {
        var result = _parser.Parse(
            "Host=localhost;Port=5433;Database=demo;Username=postgres;Password=top-secret;",
            DatabaseEngine.PostgreSql);

        Assert.Equal("localhost", result.Host);
        Assert.Equal(5433, result.Port);
        Assert.Equal("demo", result.Database);
        Assert.Equal("postgres", result.Username);
        Assert.DoesNotContain(
            result.GetType().GetProperties(),
            property => property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_ExtractsMySqlSafeFields()
    {
        var result = _parser.Parse(
            "Server=localhost;Port=3307;Database=demo;User=root;Password=top-secret;",
            DatabaseEngine.MySql);

        Assert.Equal("localhost", result.Host);
        Assert.Equal(3307, result.Port);
        Assert.Equal("demo", result.Database);
        Assert.Equal("root", result.Username);
    }

    [Theory]
    [InlineData(
        "Server=demo-mssql-target,1433;Database=demo;User Id=sa;Password=top-secret;",
        "demo-mssql-target",
        1433)]
    [InlineData(
        "Server=demo-mssql-target;Port=11433;Database=demo;User Id=sa;Password=top-secret;",
        "demo-mssql-target",
        11433)]
    public void Parse_ExtractsMsSqlSafeFields(string connectionString, string expectedHost, int expectedPort)
    {
        var result = _parser.Parse(connectionString, DatabaseEngine.MsSql);

        Assert.Equal(expectedHost, result.Host);
        Assert.Equal(expectedPort, result.Port);
        Assert.Equal("demo", result.Database);
        Assert.Equal("sa", result.Username);
        Assert.DoesNotContain(
            result.GetType().GetProperties(),
            property => property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(
        "User Id=dbscanner_scanner;Password=top-secret;Data Source=demo-oracle-target:1521/FREEPDB1;",
        "demo-oracle-target",
        1521)]
    [InlineData(
        "User Id=dbscanner_scanner;Password=top-secret;Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=oracle.internal)(PORT=11521))(CONNECT_DATA=(SERVICE_NAME=FREEPDB1)));",
        "oracle.internal",
        11521)]
    public void Parse_ExtractsOracleSafeFields(
        string connectionString,
        string expectedHost,
        int expectedPort)
    {
        var result = _parser.Parse(connectionString, DatabaseEngine.Oracle);

        Assert.Equal(expectedHost, result.Host);
        Assert.Equal(expectedPort, result.Port);
        Assert.Equal("FREEPDB1", result.Database);
        Assert.Equal("dbscanner_scanner", result.Username);
        Assert.DoesNotContain(
            result.GetType().GetProperties(),
            property => property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_InvalidConnectionString_ReturnsSanitizedError()
    {
        const string password = "must-not-leak";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            _parser.Parse(
                $"Host=localhost;Port=not-a-number;Password={password};",
                DatabaseEngine.PostgreSql));

        Assert.Equal("Connection string could not be parsed for the selected engine.", exception.Message);
        Assert.DoesNotContain(password, exception.ToString(), StringComparison.Ordinal);
    }
}
