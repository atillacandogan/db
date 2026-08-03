namespace DbScanner.Api.Contracts;

public sealed record DatabaseCredentialRequest(
    string Database,
    string Username,
    string Password,
    bool Encrypt = true,
    bool TrustServerCertificate = false,
    int ConnectionTimeoutSeconds = 5);
