using DbScanner.Domain.Enums;

namespace DbScanner.Application.Models;

public sealed record UpdateTargetCommand(
    string Name,
    string Host,
    int Port,
    DatabaseEngine Engine,
    string? EnvironmentTag,
    bool Enabled,
    DatabaseAuthentication? Credentials = null,
    bool ClearCredentials = false);
