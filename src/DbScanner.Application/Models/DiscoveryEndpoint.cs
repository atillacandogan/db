using DbScanner.Domain.Enums;

namespace DbScanner.Application.Models;

public sealed record DiscoveryEndpoint(
    string Host,
    int Port,
    DatabaseEngine? ExpectedEngine = null,
    string? ConnectAddress = null);
