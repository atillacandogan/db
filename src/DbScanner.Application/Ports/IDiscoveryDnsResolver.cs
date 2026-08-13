using System.Net;

namespace DbScanner.Application.Ports;

public interface IDiscoveryDnsResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveIpv4Async(
        string hostname,
        CancellationToken cancellationToken = default);
}
