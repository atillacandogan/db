namespace DbScanner.Application.Models;

public sealed record DiscoveryProbeResult(
    string Host,
    int Port,
    bool IsOpen,
    byte[] Banner);
