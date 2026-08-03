namespace DbScanner.Api.Contracts;

public sealed record CreateTargetRequest(
    string Name,
    string? Host,
    int? Port,
    string Engine,
    string? EnvironmentTag,
    bool Enabled = true,
    string ConnectionMode = "defaultValues",
    string? ConnectionString = null,
    DatabaseCredentialRequest? Credentials = null);
