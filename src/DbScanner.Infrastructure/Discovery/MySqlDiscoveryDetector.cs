using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Enums;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class MySqlDiscoveryDetector : IDatabaseFingerprintDetector
{
    public int Order => 10;

    public DiscoveryFingerprint? Detect(DiscoveryProbeResult probeResult)
    {
        if (probeResult.ProtocolEvidence == TcpDiscoveryProbe.MySqlHandshakeEvidence)
        {
            return new DiscoveryFingerprint(
                "MySQL",
                DatabaseEngine.MySql,
                95,
                DiscoveryFingerprintLevel.Confirmed);
        }

        if (probeResult.ProtocolEvidence is not null
            || probeResult.ExpectedEngine != DatabaseEngine.MySql
                && (probeResult.ExpectedEngine is not null || probeResult.Port != 3306))
        {
            return null;
        }

        return new DiscoveryFingerprint(
            "Unconfirmed service on a MySQL-associated port",
            null,
            30,
            DiscoveryFingerprintLevel.PortHint);
    }
}
