using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Enums;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class DiscoveryResultClassifier : IDiscoveryResultClassifier
{
    private static readonly HashSet<DatabaseEngine> SupportedEngines =
        [DatabaseEngine.MySql, DatabaseEngine.PostgreSql, DatabaseEngine.MsSql, DatabaseEngine.Oracle];

    public DiscoveryClassification Classify(
        DiscoveryFingerprint fingerprint,
        IReadOnlySet<DatabaseEngine> supportedEngineFilter)
    {
        if (fingerprint.Engine is { } engine
            && SupportedEngines.Contains(engine)
            && supportedEngineFilter.Contains(engine))
        {
            return new DiscoveryClassification(
                fingerprint.Service,
                engine,
                fingerprint.Confidence,
                DiscoveryResultStatus.Supported,
                "Supported database engine; it can be added as a target.");
        }

        var reason = fingerprint.Engine is { } filteredEngine && SupportedEngines.Contains(filteredEngine)
                ? $"{fingerprint.Service} was excluded by the supported engine filter."
                : $"{fingerprint.Service} is not supported yet.";

        return new DiscoveryClassification(
            fingerprint.Service,
            fingerprint.Engine,
            fingerprint.Confidence,
            DiscoveryResultStatus.Unsupported,
            reason);
    }
}
