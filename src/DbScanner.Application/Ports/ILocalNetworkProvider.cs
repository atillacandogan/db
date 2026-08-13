using DbScanner.Application.Models;

namespace DbScanner.Application.Ports;

public interface ILocalNetworkProvider
{
    IReadOnlyList<LocalNetworkRange> GetPrivateIpv4Networks();
}
