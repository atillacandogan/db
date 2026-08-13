namespace DbScanner.Contracts.Discovery;

public sealed record ApproveDiscoveryAllowlistResponseDto(
    int ApprovedCount,
    IReadOnlyList<DiscoveryAllowlistEntryDto> Entries);
