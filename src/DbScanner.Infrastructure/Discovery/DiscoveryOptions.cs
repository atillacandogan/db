using DbScanner.Domain.Enums;
using DbScanner.Domain.Services;

namespace DbScanner.Infrastructure.Discovery;

public sealed class DiscoveryOptions
{
    public const string SectionName = "Discovery";

    public int ConnectTimeoutMilliseconds { get; set; } = 1000;
    public int BannerReadTimeoutMilliseconds { get; set; } = 1000;
    public int DnsTimeoutMilliseconds { get; set; } = 2000;
    public int JobTimeoutSeconds { get; set; } = 300;
    public int MaxConcurrency { get; set; } = 8;
    public int MaxProbesPerSecond { get; set; } = 20;
    public int ProbeDelayMilliseconds { get; set; } = 50;
    public int MaxCidrHosts { get; set; } = 256;
    public int MaxPortsPerTarget { get; set; } = 16;
    public int MaxBannerBytes { get; set; } = 512;
    public Dictionary<string, int[]> LocalNetworkPorts { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mysql"] = [3306, 13306],
        ["postgresql"] = [5432, 15432],
        ["mssql"] = [1433, 11433],
        ["oracle"] = [1521, 11521]
    };
    public int[] UnsupportedDatabasePorts { get; set; } = [6379, 27017];

    public static bool IsValid(DiscoveryOptions options) => Validate(options).Count == 0;

    public static IReadOnlyList<string> Validate(DiscoveryOptions options)
    {
        var failures = new List<string>();
        ValidateRange(failures, nameof(ConnectTimeoutMilliseconds), options.ConnectTimeoutMilliseconds, 100, 5000);
        ValidateRange(failures, nameof(BannerReadTimeoutMilliseconds), options.BannerReadTimeoutMilliseconds, 50, 2000);
        ValidateRange(failures, nameof(DnsTimeoutMilliseconds), options.DnsTimeoutMilliseconds, 100, 10000);
        ValidateRange(failures, nameof(JobTimeoutSeconds), options.JobTimeoutSeconds, 10, 3600);
        ValidateRange(failures, nameof(MaxConcurrency), options.MaxConcurrency, 1, 32);
        ValidateRange(failures, nameof(MaxProbesPerSecond), options.MaxProbesPerSecond, 1, 100);
        ValidateRange(failures, nameof(ProbeDelayMilliseconds), options.ProbeDelayMilliseconds, 0, 10000);
        ValidateRange(failures, nameof(MaxCidrHosts), options.MaxCidrHosts, 1, 4096);
        ValidateRange(failures, nameof(MaxPortsPerTarget), options.MaxPortsPerTarget, 1, 64);
        ValidateRange(failures, nameof(MaxBannerBytes), options.MaxBannerBytes, 64, 4096);
        ValidateLocalNetworkPorts(failures, options);
        ValidateUnsupportedDatabasePorts(failures, options);
        ValidatePortBudget(failures, options);
        return failures;
    }

    public IReadOnlyList<int> GetLocalNetworkPorts(DatabaseEngine engine)
    {
        var key = engine.ToApiString();
        var configured = LocalNetworkPorts.FirstOrDefault(pair =>
            pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value ?? [];
        return configured
            .Append(DiscoveryAllowlistPolicy.StandardPorts[engine])
            .Distinct()
            .Order()
            .ToArray();
    }

    public IReadOnlyList<int> GetUnsupportedDatabasePorts() =>
        (UnsupportedDatabasePorts ?? [])
            .Distinct()
            .Order()
            .ToArray();

    private static void ValidateRange(
        ICollection<string> failures,
        string propertyName,
        int value,
        int minimum,
        int maximum)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add(
                $"Discovery:{propertyName} must be between {minimum} and {maximum}. Current value: {value}.");
        }
    }

    private static void ValidateLocalNetworkPorts(ICollection<string> failures, DiscoveryOptions options)
    {
        if (options.LocalNetworkPorts is null || options.LocalNetworkPorts.Count == 0)
        {
            failures.Add("Discovery:LocalNetworkPorts must define at least one supported database engine.");
            return;
        }

        var supported = new HashSet<string>(
            DiscoveryAllowlistPolicy.StandardPorts.Keys.Select(engine => engine.ToApiString()),
            StringComparer.OrdinalIgnoreCase);
        foreach (var (engine, ports) in options.LocalNetworkPorts)
        {
            if (!supported.Contains(engine))
            {
                failures.Add(
                    $"Discovery:LocalNetworkPorts:{engine} is not a supported database engine. " +
                    $"Supported values: {string.Join(", ", supported.Order(StringComparer.OrdinalIgnoreCase))}.");
                continue;
            }

            if (ports is not { Length: > 0 })
            {
                failures.Add($"Discovery:LocalNetworkPorts:{engine} must define at least one port.");
                continue;
            }

            var invalidPorts = ports.Where(port => port is < 1 or > 65535).Distinct().Order().ToArray();
            if (invalidPorts.Length > 0)
            {
                failures.Add(
                    $"Discovery:LocalNetworkPorts:{engine} ports must be between 1 and 65535. " +
                    $"Invalid values: {string.Join(", ", invalidPorts)}.");
            }

            if (ports.Distinct().Count() != ports.Length)
            {
                failures.Add(
                    $"Discovery:LocalNetworkPorts:{engine} cannot contain duplicate port values. " +
                    $"Current values: {string.Join(", ", ports)}.");
            }
        }
    }

    private static void ValidateUnsupportedDatabasePorts(
        ICollection<string> failures,
        DiscoveryOptions options)
    {
        var ports = options.UnsupportedDatabasePorts;
        if (ports is not { Length: > 0 })
        {
            failures.Add("Discovery:UnsupportedDatabasePorts must define at least one port.");
            return;
        }

        var invalidPorts = ports.Where(port => port is < 1 or > 65535).Distinct().Order().ToArray();
        if (invalidPorts.Length > 0)
        {
            failures.Add(
                "Discovery:UnsupportedDatabasePorts values must be between 1 and 65535. " +
                $"Invalid values: {string.Join(", ", invalidPorts)}.");
        }

        if (ports.Distinct().Count() != ports.Length)
        {
            failures.Add(
                "Discovery:UnsupportedDatabasePorts cannot contain duplicate values. " +
                $"Current values: {string.Join(", ", ports)}.");
        }

        var configuredSupportedPorts = options.LocalNetworkPorts is null
            ? Enumerable.Empty<int[]>()
            : options.LocalNetworkPorts.Values;
        var supportedPorts = configuredSupportedPorts
            .SelectMany(configured => configured ?? [])
            .ToHashSet();
        var overlaps = ports.Where(supportedPorts.Contains).Distinct().Order().ToArray();
        if (overlaps.Length > 0)
        {
            failures.Add(
                "Discovery:UnsupportedDatabasePorts cannot overlap supported engine ports. " +
                $"Overlapping values: {string.Join(", ", overlaps)}.");
        }
    }

    private static void ValidatePortBudget(ICollection<string> failures, DiscoveryOptions options)
    {
        var totalPorts = (options.LocalNetworkPorts?.Values.Sum(ports => ports?.Length ?? 0) ?? 0)
            + (options.UnsupportedDatabasePorts?.Length ?? 0);
        if (totalPorts > options.MaxPortsPerTarget)
        {
            failures.Add(
                $"Discovery database port configuration defines {totalPorts} ports, but " +
                $"Discovery:MaxPortsPerTarget is {options.MaxPortsPerTarget}.");
        }
    }
}
