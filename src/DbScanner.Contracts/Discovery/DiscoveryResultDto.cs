namespace DbScanner.Contracts.Discovery;

public sealed record DiscoveryResultDto(
    Guid Id,
    string Host,
    int Port,
    string DetectedService,
    string? DetectedEngine,
    int Confidence,
    string Environment,
    string Status,
    string Reason,
    bool CanAddAsTarget,
    Guid? TargetId);
