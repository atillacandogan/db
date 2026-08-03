using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Enums;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class OracleDiscoveryDetector : IDatabaseFingerprintDetector
{
    public int Order => 40;

    public DiscoveryFingerprint? Detect(DiscoveryProbeResult probeResult) =>
        probeResult.Port == 1521
            ? new DiscoveryFingerprint("Oracle Database Listener", DatabaseEngine.Oracle, 70)
            : null;
}
