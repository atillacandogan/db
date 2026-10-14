namespace DbScanner.Contracts.AdminUsers;

public sealed record AdminUserDto(
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
