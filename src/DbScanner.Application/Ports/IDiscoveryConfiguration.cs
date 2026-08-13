namespace DbScanner.Application.Ports;

public interface IDiscoveryConfiguration
{
    int MaxCidrHosts { get; }
    int MaxPortsPerTarget { get; }
}
