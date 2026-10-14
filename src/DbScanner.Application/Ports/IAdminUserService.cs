using DbScanner.Application.Models;
using DbScanner.Contracts.AdminUsers;

namespace DbScanner.Application.Ports;

public interface IAdminUserService
{
    Task<AdminUserPageDto> ListAsync(
        string? search,
        string? role,
        bool? enabled,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<AdminUserDto?> GetAsync(string userId, CancellationToken cancellationToken = default);

    Task<AdminUserCreationDto> CreateAsync(
        CreateAdminUserCommand command,
        AdminUserActor actor,
        CancellationToken cancellationToken = default);

    Task<AdminUserDto> UpdateAsync(
        string userId,
        UpdateAdminUserCommand command,
        AdminUserActor actor,
        CancellationToken cancellationToken = default);

    Task<AdminUserDto> ChangeRoleAsync(
        string userId,
        string role,
        AdminUserActor actor,
        CancellationToken cancellationToken = default);

    Task<AdminUserDto> ChangeStatusAsync(
        string userId,
        bool enabled,
        AdminUserActor actor,
        CancellationToken cancellationToken = default);

    Task<AdminUserDto> ChangeMfaRequirementAsync(
        string userId,
        bool required,
        AdminUserActor actor,
        CancellationToken cancellationToken = default);

    Task RequestPasswordResetAsync(
        string userId,
        AdminUserActor actor,
        CancellationToken cancellationToken = default);

    Task LogoutAsync(
        string userId,
        AdminUserActor actor,
        CancellationToken cancellationToken = default);
}
