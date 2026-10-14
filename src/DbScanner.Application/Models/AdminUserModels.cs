namespace DbScanner.Application.Models;

public sealed record AdminUserActor(
    string Subject,
    string? Username,
    string? Email);

public sealed record CreateAdminUserCommand(
    string Username,
    string? FirstName,
    string? LastName,
    string? Email,
    string Role,
    bool RequireMfa,
    string TemporaryPassword)
{
    public override string ToString() =>
        $"{nameof(CreateAdminUserCommand)} {{ Username = {Username}, TemporaryPassword = [REDACTED] }}";
}

public sealed record UpdateAdminUserCommand(
    string? FirstName,
    string? LastName,
    string? Email);

public sealed record IdentityUserPage(
    IReadOnlyList<IdentityUser> Items,
    int TotalCount);

public sealed record IdentityUser(
    string Id,
    string Username,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Role,
    bool Enabled,
    bool EmailVerified,
    bool? MfaConfigured,
    bool MfaRequired,
    DateTimeOffset? LastLoginAt);
