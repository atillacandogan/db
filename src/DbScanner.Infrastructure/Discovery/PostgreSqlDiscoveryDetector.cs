using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Enums;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class PostgreSqlDiscoveryDetector : IDatabaseFingerprintDetector
{
    public int Order => 20;

    public DiscoveryFingerprint? Detect(DiscoveryProbeResult probeResult)
    {
        if (probeResult.Port != 5432)
        {
            return null;
        }

        var banner = DiscoveryDetectorText.Banner(probeResult.Banner);
        return new DiscoveryFingerprint(
            "PostgreSQL",
            DatabaseEngine.PostgreSql,
            banner.Contains("postgres", StringComparison.OrdinalIgnoreCase) ? 95 : 70);
    }
}
