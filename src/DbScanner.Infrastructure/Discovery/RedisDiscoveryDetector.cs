using DbScanner.Application.Models;
using DbScanner.Application.Ports;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class RedisDiscoveryDetector : IDatabaseFingerprintDetector
{
    public int Order => 60;

    public DiscoveryFingerprint? Detect(DiscoveryProbeResult probeResult) =>
        probeResult.Port == 6379
            ? new DiscoveryFingerprint("Redis", null, 65)
            : null;
}
