namespace DbScanner.Contracts.Discovery;

public sealed record LocalNetworkDto(
    string InterfaceName,
    string Address,
    string Cidr,
    int HostCount,
    bool CanScan,
    string? Reason);
