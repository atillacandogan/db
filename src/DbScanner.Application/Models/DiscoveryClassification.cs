using DbScanner.Domain.Enums;

namespace DbScanner.Application.Models;

public sealed record DiscoveryClassification(
    string Service,
    DatabaseEngine? Engine,
    int Confidence,
    DiscoveryResultStatus Status,
    string Reason);
