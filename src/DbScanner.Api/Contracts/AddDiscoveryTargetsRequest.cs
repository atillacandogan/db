namespace DbScanner.Api.Contracts;

public sealed record AddDiscoveryTargetsRequest(IReadOnlyList<Guid> ResultIds);
