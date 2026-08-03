namespace DbScanner.Api.Contracts;

public sealed record CreateDiscoveryRequest(
    string Name,
    string Scope,
    string Environment,
    IReadOnlyList<string>? SupportedEngineFilter = null);
