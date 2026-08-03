using DbScanner.Application.Models;
using DbScanner.Application.Ports;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class MongoDbDiscoveryDetector : IDatabaseFingerprintDetector
{
    public int Order => 50;

    public DiscoveryFingerprint? Detect(DiscoveryProbeResult probeResult) =>
        probeResult.Port == 27017
            ? new DiscoveryFingerprint("MongoDB", null, 65)
            : null;
}
