namespace DbScanner.Contracts.Discovery;

public sealed record DiscoveryResultDto(
    Guid Id,
    string Host,
    int Port,
    string DetectedService,
    string? DetectedEngine,
    int Confidence,
    string FingerprintLevel,
    string Environment,
    string Status,
    string Reason,
    bool CanAddAsTarget,
    Guid? TargetId,
    bool IsAlreadyTarget,
    Guid? ExistingTargetId,
    string TargetState);
