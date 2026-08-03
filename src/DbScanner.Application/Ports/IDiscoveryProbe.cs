using DbScanner.Application.Models;

namespace DbScanner.Application.Ports;

public interface IDiscoveryProbe
{
    Task<DiscoveryProbeResult> ProbeAsync(DiscoveryEndpoint endpoint, CancellationToken cancellationToken = default);
}
