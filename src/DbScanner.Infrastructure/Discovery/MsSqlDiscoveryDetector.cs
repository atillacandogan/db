using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Enums;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class MsSqlDiscoveryDetector : IDatabaseFingerprintDetector
{
    public int Order => 30;

    public DiscoveryFingerprint? Detect(DiscoveryProbeResult probeResult)
    {
        if (probeResult.ProtocolEvidence == TcpDiscoveryProbe.MsSqlPreLoginEvidence)
        {
            return new DiscoveryFingerprint(
                "Microsoft SQL Server",
                DatabaseEngine.MsSql,
                98,
                DiscoveryFingerprintLevel.Confirmed);
        }

        if (probeResult.ProtocolEvidence is not null
            || probeResult.ExpectedEngine != DatabaseEngine.MsSql
                && (probeResult.ExpectedEngine is not null || probeResult.Port != 1433))
        {
            return null;
        }

        return new DiscoveryFingerprint(
            "Unconfirmed service on an MSSQL-associated port",
            null,
            30,
            DiscoveryFingerprintLevel.PortHint);
    }
}
