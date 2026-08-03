using DbScanner.Domain.Enums;

namespace DbScanner.Application.Models;

public sealed record CreateTargetCommand(
    string Name,
    string? Host,
    int? Port,
    DatabaseEngine Engine,
    string? EnvironmentTag,
    bool Enabled,
    TargetConnectionMode ConnectionMode = TargetConnectionMode.DefaultValues,
    string? ConnectionString = null,
    DatabaseAuthentication? Credentials = null);
