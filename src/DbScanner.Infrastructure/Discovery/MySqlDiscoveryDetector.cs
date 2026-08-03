using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Enums;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class MySqlDiscoveryDetector : IDatabaseFingerprintDetector
{
    public int Order => 10;

    public DiscoveryFingerprint? Detect(DiscoveryProbeResult probeResult)
    {
        if (probeResult.Port != 3306)
        {
            return null;
        }

        var banner = DiscoveryDetectorText.Banner(probeResult.Banner);
        var hasProtocolFingerprint = banner.Contains("mysql", StringComparison.OrdinalIgnoreCase)
            || banner.Contains("mariadb", StringComparison.OrdinalIgnoreCase)
            || probeResult.Banner.Length > 5 && probeResult.Banner[4] == 10;
        return new DiscoveryFingerprint("MySQL", DatabaseEngine.MySql, hasProtocolFingerprint ? 95 : 65);
    }
}
