using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Enums;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class PostgreSqlDiscoveryDetector : IDatabaseFingerprintDetector
{
    public int Order => 20;

    public DiscoveryFingerprint? Detect(DiscoveryProbeResult probeResult)
    {
        if (probeResult.ProtocolEvidence == TcpDiscoveryProbe.PostgreSqlSslEvidence)
        {
            return new DiscoveryFingerprint(
                "PostgreSQL",
                DatabaseEngine.PostgreSql,
                98,
                DiscoveryFingerprintLevel.Confirmed);
        }

        if (probeResult.ProtocolEvidence is not null
            || probeResult.ExpectedEngine != DatabaseEngine.PostgreSql
                && (probeResult.ExpectedEngine is not null || probeResult.Port != 5432))
        {
            return null;
        }

        return new DiscoveryFingerprint(
            "Unconfirmed service on a PostgreSQL-associated port",
            null,
            30,
            DiscoveryFingerprintLevel.PortHint);
    }
}
