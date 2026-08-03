using DbScanner.Domain.Enums;

namespace DbScanner.Application.Models;

public sealed record DiscoveryFingerprint(
    string Service,
    DatabaseEngine? Engine,
    int Confidence);
