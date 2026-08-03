namespace DbScanner.Infrastructure.Discovery;

public sealed class DiscoveryOptions
{
    public const string SectionName = "Discovery";

    public int ConnectTimeoutMilliseconds { get; set; } = 1000;
    public int BannerReadTimeoutMilliseconds { get; set; } = 200;
    public int MaxConcurrency { get; set; } = 8;
    public int MaxProbesPerSecond { get; set; } = 20;
    public int MaxHosts { get; set; } = 256;
    public int MaxBannerBytes { get; set; } = 512;
}
