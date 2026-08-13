using DbScanner.Application.Ports;
using Microsoft.Extensions.Options;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class DiscoveryConfiguration(IOptions<DiscoveryOptions> options) : IDiscoveryConfiguration
{
    public int MaxCidrHosts => options.Value.MaxCidrHosts;
    public int MaxPortsPerTarget => options.Value.MaxPortsPerTarget;
}
