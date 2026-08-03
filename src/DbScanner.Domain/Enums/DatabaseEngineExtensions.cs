namespace DbScanner.Domain.Enums;

public static class DatabaseEngineExtensions
{
    public static string ToApiString(this DatabaseEngine engine) => engine switch
    {
        DatabaseEngine.PostgreSql => "postgresql",
        DatabaseEngine.MySql => "mysql",
        DatabaseEngine.MsSql => "mssql",
        DatabaseEngine.Oracle => "oracle",
        _ => "generic"
    };

    public static string ToDisplayName(this DatabaseEngine engine) => engine switch
    {
        DatabaseEngine.PostgreSql => "PostgreSQL",
        DatabaseEngine.MySql => "MySQL",
        DatabaseEngine.MsSql => "Microsoft SQL Server",
        DatabaseEngine.Oracle => "Oracle Database",
        _ => "Generic"
    };

    public static bool TryParseApiString(string? value, out DatabaseEngine engine)
    {
        engine = value?.Trim().ToLowerInvariant() switch
        {
            "postgresql" => DatabaseEngine.PostgreSql,
            "mysql" => DatabaseEngine.MySql,
            "mssql" => DatabaseEngine.MsSql,
            "oracle" => DatabaseEngine.Oracle,
            "generic" => DatabaseEngine.Generic,
            _ => (DatabaseEngine)(-1)
        };

        return Enum.IsDefined(engine);
    }
}
