using DbScanner.Application.Models;
using DbScanner.Application.Ports;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class UnknownTcpDiscoveryDetector : IDatabaseFingerprintDetector
{
    public int Order => int.MaxValue;

    public DiscoveryFingerprint? Detect(DiscoveryProbeResult probeResult) =>
        probeResult.IsOpen
            ? new DiscoveryFingerprint("Unknown TCP service", null, 25)
            : null;
}
