using System.Net;
using System.Net.Sockets;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Domain.Services;

namespace DbScanner.Infrastructure.Discovery;

internal static class NetworkScope
{
    public static bool IsHostAllowed(
        string host,
        int port,
        DatabaseEngine engine,
        IEnumerable<ScanScopeAllowlist> allowlist) =>
        allowlist.Any(scope => scope.Enabled
            && EntryAllowsEnginePort(scope, engine, port)
            && EntryContainsHost(scope, host));

    public static bool EntryAllowsEnginePort(
        ScanScopeAllowlist entry,
        DatabaseEngine engine,
        int port)
    {
        if (!DiscoveryAllowlistPolicy.ParseEngines(entry.AllowedEngines).Contains(engine))
        {
            return false;
        }

        var ports = DiscoveryAllowlistPolicy.ParsePorts(entry);
        return ports.TryGetValue(engine, out var allowedPorts)
            ? allowedPorts.Contains(port)
            : entry.Port is null || entry.Port == port;
    }

    public static IReadOnlyList<int> GetPorts(ScanScopeAllowlist entry, DatabaseEngine engine)
    {
        var ports = DiscoveryAllowlistPolicy.ParsePorts(entry);
        if (ports.TryGetValue(engine, out var configured))
        {
            return configured;
        }

        return entry.Port is { } legacyPort
            ? [legacyPort]
            : [DiscoveryAllowlistPolicy.StandardPorts[engine]];
    }

    public static bool EntryContainsHost(ScanScopeAllowlist entry, string host)
    {
        if (!IPAddress.TryParse(host, out var address))
        {
            return entry.ScopeType is ScanScopeType.Hostname or ScanScopeType.DemoTarget
                && entry.ScopeValue.Equals(host.Trim().TrimEnd('.'), StringComparison.OrdinalIgnoreCase);
        }

        return entry.ScopeType == ScanScopeType.IpAddress
                && IPAddress.TryParse(entry.ScopeValue, out var allowedAddress)
                && allowedAddress.Equals(address)
            || entry.ScopeType == ScanScopeType.Cidr
                && TryParseIpv4Cidr(entry.ScopeValue, out var network, out var prefix)
                && Contains(network, prefix, address);
    }

    public static bool TryParseIpv4Cidr(string value, out IPAddress network, out int prefixLength)
    {
        network = IPAddress.None;
        prefixLength = 0;
        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var address)
            || address.AddressFamily != AddressFamily.InterNetwork
            || !int.TryParse(parts[1], out prefixLength)
            || prefixLength is < 0 or > 32)
        {
            return false;
        }

        var mask = PrefixMask(prefixLength);
        network = FromUInt32(ToUInt32(address) & mask);
        return true;
    }

    public static bool Contains(IPAddress network, int prefixLength, IPAddress address)
    {
        if (network.AddressFamily != AddressFamily.InterNetwork
            || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var mask = PrefixMask(prefixLength);
        return (ToUInt32(network) & mask) == (ToUInt32(address) & mask);
    }

    public static bool ContainsCidr(
        IPAddress outerNetwork,
        int outerPrefix,
        IPAddress innerNetwork,
        int innerPrefix) =>
        innerPrefix >= outerPrefix && Contains(outerNetwork, outerPrefix, innerNetwork);

    public static long GetIpv4AddressCount(int prefixLength) => 1L << (32 - prefixLength);

    public static IReadOnlyList<string> ExpandIpv4(
        IPAddress network,
        int prefixLength,
        int maxHosts)
    {
        var addressCount = GetIpv4AddressCount(prefixLength);
        if (addressCount > maxHosts)
        {
            throw new InvalidOperationException($"Discovery CIDR cannot contain more than {maxHosts} addresses.");
        }

        var first = ToUInt32(network);
        var skipNetworkAndBroadcast = prefixLength <= 30;
        var start = skipNetworkAndBroadcast ? 1L : 0L;
        var endExclusive = skipNetworkAndBroadcast ? addressCount - 1 : addressCount;
        var addresses = new List<string>((int)Math.Max(0, endExclusive - start));
        for (var offset = start; offset < endExclusive; offset++)
        {
            addresses.Add(FromUInt32(first + (uint)offset).ToString());
        }

        return addresses;
    }

    public static bool IsPrivateUnicast(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork
            || IPAddress.IsLoopback(address)
            || IsLinkLocal(address)
            || IsMulticast(address)
            || address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.Broadcast))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || bytes[0] == 192 && bytes[1] == 168
            || bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
    }

    public static bool IsPrivateNetwork(IPAddress network)
    {
        if (network.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = network.GetAddressBytes();
        return bytes[0] == 10
            || bytes[0] == 192 && bytes[1] == 168
            || bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
    }

    public static bool IsLinkLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254;
    }

    public static bool IsMulticast(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes.Length == 4 && bytes[0] is >= 224 and <= 239;
    }

    private static uint PrefixMask(int prefixLength) =>
        prefixLength == 0 ? 0 : uint.MaxValue << (32 - prefixLength);

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress FromUInt32(uint value) =>
        new([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
}
