using System.Net;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Infrastructure.Discovery;

internal static class NetworkScope
{
    public static bool IsHostAllowed(
        string host,
        int port,
        IEnumerable<ScanScopeAllowlist> allowlist)
    {
        if (!IPAddress.TryParse(host, out var address))
        {
            return allowlist.Any(scope =>
                scope.Enabled
                && PortMatches(scope, port)
                && scope.ScopeType is ScanScopeType.Hostname or ScanScopeType.DemoTarget
                && scope.ScopeValue.Equals(host, StringComparison.OrdinalIgnoreCase));
        }

        return allowlist.Any(scope =>
            scope.Enabled
            && PortMatches(scope, port)
            && (scope.ScopeType == ScanScopeType.IpAddress
                    && IPAddress.TryParse(scope.ScopeValue, out var allowedAddress)
                    && allowedAddress.Equals(address)
                || scope.ScopeType == ScanScopeType.Cidr
                    && TryParseIpv4Cidr(scope.ScopeValue, out var network, out var prefix)
                    && Contains(network, prefix, address)));
    }

    public static bool TryParseIpv4Cidr(string value, out IPAddress network, out int prefixLength)
    {
        network = IPAddress.None;
        prefixLength = 0;
        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var address)
            || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
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
        if (network.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
            || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
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

    public static IReadOnlyList<string> ExpandIpv4(IPAddress network, int prefixLength, int maxHosts)
    {
        var addressCount = 1L << (32 - prefixLength);
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

    public static bool IsPrivateOrLocal(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || bytes[0] == 127
            || bytes[0] == 192 && bytes[1] == 168
            || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
            || bytes[0] == 169 && bytes[1] == 254;
    }

    private static bool PortMatches(ScanScopeAllowlist scope, int port) =>
        scope.Port is null || scope.Port == port;

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
