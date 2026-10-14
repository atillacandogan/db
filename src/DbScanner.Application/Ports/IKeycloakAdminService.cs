using DbScanner.Application.Models;

namespace DbScanner.Application.Ports;

public interface IKeycloakAdminService
{
    Task<IdentityUserPage> ListUsersAsync(
        string? search,
        string? role,
        bool? enabled,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<IdentityUser?> GetUserAsync(string userId, CancellationToken cancellationToken = default);

    Task<IdentityUser> CreateUserAsync(
        CreateAdminUserCommand command,
        CancellationToken cancellationToken = default);

    Task<IdentityUser> UpdateUserAsync(
        string userId,
        UpdateAdminUserCommand command,
        CancellationToken cancellationToken = default);

    Task<IdentityUser> SetUserRoleAsync(
        string userId,
        string role,
        CancellationToken cancellationToken = default);

    Task<IdentityUser> SetUserEnabledAsync(
        string userId,
        bool enabled,
        CancellationToken cancellationToken = default);

    Task<IdentityUser> SetMfaRequiredAsync(
        string userId,
        bool required,
        CancellationToken cancellationToken = default);

    Task<int> CountActiveUsersWithRoleAsync(
        string role,
        CancellationToken cancellationToken = default);

    Task SendActionsEmailAsync(
        string userId,
        IReadOnlyCollection<string> actions,
        CancellationToken cancellationToken = default);

    Task LogoutUserAsync(string userId, CancellationToken cancellationToken = default);
}
