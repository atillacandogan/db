namespace DbScanner.Application.Models;

public static class DiscoveryScopeSelection
{
    public const string AllAuthorized = "__all_authorized__";
    private const string LocalNetworkPrefix = "__local_network__:";

    public static bool IsAllAuthorized(string scopeInput) =>
        scopeInput.Equals(AllAuthorized, StringComparison.Ordinal);

    public static string LocalNetworks(IEnumerable<string> cidrs) =>
        LocalNetworkPrefix + string.Join(',', cidrs.Distinct(StringComparer.OrdinalIgnoreCase).Order());

    public static bool TryGetLocalNetworks(string scopeInput, out IReadOnlyList<string> cidrs)
    {
        cidrs = [];
        if (!scopeInput.StartsWith(LocalNetworkPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        cidrs = scopeInput[LocalNetworkPrefix.Length..]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return cidrs.Count > 0;
    }
}
