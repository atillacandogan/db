namespace DbScanner.Api.Contracts;

public sealed record CreateAdminUserRequest(
    string Username,
    string? FirstName,
    string? LastName,
    string? Email,
    string Role,
    bool RequireMfa,
    string TemporaryPassword)
{
    public override string ToString() =>
        $"{nameof(CreateAdminUserRequest)} {{ Username = {Username}, TemporaryPassword = [REDACTED] }}";
}

public sealed record UpdateAdminUserRequest(
    string? FirstName,
    string? LastName,
    string? Email);

public sealed record UpdateAdminUserRoleRequest(string Role);

public sealed record UpdateAdminUserStatusRequest(bool Enabled);

public sealed record UpdateAdminUserMfaRequest(bool Required);
