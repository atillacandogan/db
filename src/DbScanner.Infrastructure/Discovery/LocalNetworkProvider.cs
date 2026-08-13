using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using Microsoft.Extensions.Options;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class LocalNetworkProvider(IOptions<DiscoveryOptions> options) : ILocalNetworkProvider
{
    private static readonly string[] ExcludedInterfaceMarkers =
    [
        "docker", "veth", "vethernet", "hyper-v", "wsl", "vmware", "virtualbox",
        "vpn", "tunnel", "tailscale", "zerotier", "loopback"
    ];

    public IReadOnlyList<LocalNetworkRange> GetPrivateIpv4Networks()
    {
        if (string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
            "true",
            StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var ranges = new List<LocalNetworkRange>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up
                || IsExcludedInterface(networkInterface.Name, networkInterface.Description, networkInterface.NetworkInterfaceType))
            {
                continue;
            }

            IPInterfaceProperties properties;
            try
            {
                properties = networkInterface.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            foreach (var unicast in properties.UnicastAddresses.Where(item =>
                         item.Address.AddressFamily == AddressFamily.InterNetwork
                         && NetworkScope.IsPrivateUnicast(item.Address)))
            {
                var prefix = unicast.PrefixLength;
                if (prefix is < 1 or > 32
                    || !NetworkScope.TryParseIpv4Cidr($"{unicast.Address}/{prefix}", out var network, out _))
                {
                    continue;
                }

                var addressCount = NetworkScope.GetIpv4AddressCount(prefix);
                var usableCount = prefix <= 30 ? Math.Max(0, addressCount - 2) : addressCount;
                var canScan = addressCount <= options.Value.MaxCidrHosts;
                ranges.Add(new LocalNetworkRange(
                    networkInterface.Name,
                    unicast.Address.ToString(),
                    $"{network}/{prefix}",
                    checked((int)Math.Min(usableCount, int.MaxValue)),
                    canScan,
                    canScan
                        ? null
                        : $"Network contains more than the configured {options.Value.MaxCidrHosts} address safety limit."));
            }
        }

        return ranges
            .GroupBy(range => range.Cidr, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(range => range.Cidr, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static bool IsExcludedInterface(
        string name,
        string description,
        NetworkInterfaceType interfaceType)
    {
        if (interfaceType is not (NetworkInterfaceType.Ethernet
            or NetworkInterfaceType.Wireless80211
            or NetworkInterfaceType.GigabitEthernet
            or NetworkInterfaceType.FastEthernetFx
            or NetworkInterfaceType.FastEthernetT))
        {
            return true;
        }

        var identity = $"{name} {description}";
        return ExcludedInterfaceMarkers.Any(marker =>
            identity.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}
