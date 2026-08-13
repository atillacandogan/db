using DbScanner.Application.Models;
using DbScanner.Application.Ports;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class RedisDiscoveryDetector : IDatabaseFingerprintDetector
{
    public int Order => 60;

    public DiscoveryFingerprint? Detect(DiscoveryProbeResult probeResult) =>
        probeResult.ProtocolEvidence is null && probeResult.Port == 6379
            ? new DiscoveryFingerprint("Redis", null, 30, DbScanner.Domain.Enums.DiscoveryFingerprintLevel.PortHint)
            : null;
}
