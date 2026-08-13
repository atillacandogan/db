using System.Net;
using System.Net.Sockets;
using DbScanner.Application.Ports;
using Microsoft.Extensions.Options;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class DiscoveryDnsResolver(IOptions<DiscoveryOptions> options) : IDiscoveryDnsResolver
{
    public async Task<IReadOnlyList<IPAddress>> ResolveIpv4Async(
        string hostname,
        CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.DnsTimeoutMilliseconds);
        try
        {
            return (await Dns.GetHostAddressesAsync(hostname, timeout.Token))
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Distinct()
                .OrderBy(address => address.ToString(), StringComparer.Ordinal)
                .ToArray();
        }
        catch (SocketException)
        {
            return [];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }
}
