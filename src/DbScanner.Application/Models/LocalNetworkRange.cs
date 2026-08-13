namespace DbScanner.Application.Models;

public sealed record LocalNetworkRange(
    string InterfaceName,
    string Address,
    string Cidr,
    int HostCount,
    bool CanScan,
    string? Reason = null);
