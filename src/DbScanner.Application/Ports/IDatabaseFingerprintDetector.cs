using DbScanner.Application.Models;

namespace DbScanner.Application.Ports;

public interface IDatabaseFingerprintDetector
{
    int Order { get; }
    DiscoveryFingerprint? Detect(DiscoveryProbeResult probeResult);
}
