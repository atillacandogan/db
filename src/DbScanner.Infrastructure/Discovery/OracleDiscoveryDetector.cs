using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Enums;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class OracleDiscoveryDetector : IDatabaseFingerprintDetector
{
    public int Order => 40;

    public DiscoveryFingerprint? Detect(DiscoveryProbeResult probeResult)
    {
        if (probeResult.ProtocolEvidence == TcpDiscoveryProbe.OracleTnsEvidence)
        {
            return new DiscoveryFingerprint(
                "Oracle Database Listener",
                DatabaseEngine.Oracle,
                98,
                DiscoveryFingerprintLevel.Confirmed);
        }

        if (probeResult.ProtocolEvidence is not null
            || probeResult.ExpectedEngine != DatabaseEngine.Oracle
                && (probeResult.ExpectedEngine is not null || probeResult.Port != 1521))
        {
            return null;
        }

        return new DiscoveryFingerprint(
            "Unconfirmed service on an Oracle-associated port",
            null,
            30,
            DiscoveryFingerprintLevel.PortHint);
    }
}
