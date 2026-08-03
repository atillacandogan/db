namespace DbScanner.Api.Contracts;

public sealed record UpdateTargetRequest(
    string Name,
    string Host,
    int Port,
    string Engine,
    string? EnvironmentTag,
    bool Enabled = true,
    DatabaseCredentialRequest? Credentials = null,
    bool ClearCredentials = false);
