namespace DbScanner.Contracts.Discovery;

public sealed record DiscoveryRequestedMessage(
    Guid DiscoveryJobId,
    DateTimeOffset RequestedAt,
    string? TenantId = null);
