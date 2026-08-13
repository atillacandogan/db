using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Domain.Services;

public static class DiscoveryAllowlistPolicy
{
    public static readonly IReadOnlyDictionary<DatabaseEngine, int> StandardPorts =
        new Dictionary<DatabaseEngine, int>
        {
            [DatabaseEngine.MySql] = 3306,
            [DatabaseEngine.PostgreSql] = 5432,
            [DatabaseEngine.MsSql] = 1433,
            [DatabaseEngine.Oracle] = 1521
        };

    public static HashSet<DatabaseEngine> ParseEngines(string value)
    {
        var engines = new HashSet<DatabaseEngine>();
        foreach (var token in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (DatabaseEngineExtensions.TryParseApiString(token, out var engine)
                && engine is DatabaseEngine.MySql or DatabaseEngine.PostgreSql or DatabaseEngine.MsSql or DatabaseEngine.Oracle)
            {
                engines.Add(engine);
            }
        }

        return engines;
    }

    public static IReadOnlyDictionary<DatabaseEngine, IReadOnlyList<int>> ParsePorts(ScanScopeAllowlist entry)
    {
        var parsed = new Dictionary<DatabaseEngine, IReadOnlyList<int>>();
        foreach (var group in entry.AllowedPorts.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = group.IndexOf(':');
            if (separator <= 0
                || !DatabaseEngineExtensions.TryParseApiString(group[..separator], out var engine))
            {
                continue;
            }

            var ports = group[(separator + 1)..]
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(value => int.TryParse(value, out var port) ? port : 0)
                .Where(port => port is >= 1 and <= 65535)
                .Distinct()
                .Order()
                .ToArray();
            if (ports.Length > 0)
            {
                parsed[engine] = ports;
            }
        }

        if (parsed.Count == 0 && entry.Port is { } legacyPort)
        {
            foreach (var engine in ParseEngines(entry.AllowedEngines))
            {
                if (StandardPorts[engine] == legacyPort)
                {
                    parsed[engine] = [legacyPort];
                }
            }
        }

        return parsed;
    }

    public static string SerializeEngines(IEnumerable<DatabaseEngine> engines) =>
        string.Join(',', engines.Distinct().OrderBy(engine => engine).Select(engine => engine.ToApiString()));

    public static string SerializePorts(IReadOnlyDictionary<DatabaseEngine, IReadOnlyList<int>> ports) =>
        string.Join(
            ';',
            ports.OrderBy(pair => pair.Key).Select(pair =>
                $"{pair.Key.ToApiString()}:{string.Join(',', pair.Value.Distinct().Order())}"));
}
