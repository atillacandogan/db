namespace DbScanner.Application.Models;

public sealed record DatabaseAuthentication(
    string Database,
    string Username,
    string Password,
    bool Encrypt = true,
    bool TrustServerCertificate = false,
    int ConnectionTimeoutSeconds = 5);
