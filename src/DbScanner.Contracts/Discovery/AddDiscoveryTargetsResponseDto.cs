namespace DbScanner.Contracts.Discovery;

public sealed record AddDiscoveryTargetsResponseDto(
    int AddedCount,
    int AlreadyExistingCount,
    int FailedCount,
    IReadOnlyList<DiscoveryTargetAddItemDto> Items,
    IReadOnlyList<DiscoveryResultDto> Results);
