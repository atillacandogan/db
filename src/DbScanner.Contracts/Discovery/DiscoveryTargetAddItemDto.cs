namespace DbScanner.Contracts.Discovery;

public sealed record DiscoveryTargetAddItemDto(
    Guid ResultId,
    string Status,
    string? Code,
    string? Message,
    Guid? TargetId);
