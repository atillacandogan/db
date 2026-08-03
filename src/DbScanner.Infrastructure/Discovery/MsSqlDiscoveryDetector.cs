using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Enums;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class MsSqlDiscoveryDetector : IDatabaseFingerprintDetector
{
    public int Order => 30;

    public DiscoveryFingerprint? Detect(DiscoveryProbeResult probeResult) =>
        probeResult.Port == 1433
            ? new DiscoveryFingerprint("Microsoft SQL Server", DatabaseEngine.MsSql, 70)
            : null;
}
