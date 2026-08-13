using DbScanner.Domain.Enums;

namespace DbScanner.Application.Models;

public sealed record DiscoveryClassification(
    string Service,
    DatabaseEngine? Engine,
    int Confidence,
    DiscoveryFingerprintLevel FingerprintLevel,
    DiscoveryResultStatus Status,
    string Reason);
