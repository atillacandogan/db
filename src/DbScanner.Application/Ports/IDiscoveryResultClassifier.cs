using DbScanner.Application.Models;
using DbScanner.Domain.Enums;

namespace DbScanner.Application.Ports;

public interface IDiscoveryResultClassifier
{
    DiscoveryClassification Classify(
        DiscoveryFingerprint fingerprint,
        IReadOnlySet<DatabaseEngine> supportedEngineFilter);
}
