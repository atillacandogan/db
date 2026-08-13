using DbScanner.Application.Models;
using DbScanner.Application.Ports;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class MongoDbDiscoveryDetector : IDatabaseFingerprintDetector
{
    public int Order => 50;

    public DiscoveryFingerprint? Detect(DiscoveryProbeResult probeResult) =>
        probeResult.ProtocolEvidence is null && probeResult.Port == 27017
            ? new DiscoveryFingerprint("MongoDB", null, 30, DbScanner.Domain.Enums.DiscoveryFingerprintLevel.PortHint)
            : null;
}
