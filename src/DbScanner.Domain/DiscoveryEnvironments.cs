namespace DbScanner.Domain;

public static class DiscoveryEnvironments
{
    public const string Demo = "demo";
    public const string Test = "test";
    public const string Staging = "staging";
    public const string Production = "prod";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [Demo, Test, Staging, Production],
        StringComparer.OrdinalIgnoreCase);

    public static bool TryNormalize(string? value, out string environment)
    {
        environment = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return All.Contains(environment);
    }
}
