namespace DbScanner.Api.Contracts;

public sealed record CreateLocalNetworkDiscoveryRequest(
    string Name,
    string Environment,
    IReadOnlyList<string>? SupportedEngineFilter = null,
    IReadOnlyList<string>? Networks = null);

public sealed record ApproveDiscoveryAllowlistRequest(IReadOnlyList<Guid> ResultIds);
