using System.Net.Mail;
using DbScanner.Application.Exceptions;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Contracts.AdminUsers;
using DbScanner.Domain.Entities;

namespace DbScanner.Application.Services;

public sealed class AdminUserService(
    IKeycloakAdminService keycloakAdminService,
    IUserManagementAuditRepository auditRepository) : IAdminUserService
{
    public const int UsernameMinLength = 3;
    public const int UsernameMaxLength = 128;
    public const int TemporaryPasswordMinLength = 12;
    public const int TemporaryPasswordMaxLength = 128;
    public const string ViewerRole = "dbscanner-viewer";
    public const string OperatorRole = "dbscanner-operator";
    public const string AdminRole = "dbscanner-admin";

    private static readonly HashSet<string> ManagedRoles =
        [ViewerRole, OperatorRole, AdminRole];
    private static readonly SemaphoreSlim AdminMutationLock = new(1, 1);

    public async Task<AdminUserPageDto> ListAsync(
        string? search,
        string? role,
        bool? enabled,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            throw new InvalidOperationException("Page must be at least 1.");
        }

        if (pageSize is < 1 or > 100)
        {
            throw new InvalidOperationException("Page size must be between 1 and 100.");
        }

        var result = await keycloakAdminService.ListUsersAsync(
            NormalizeOptional(search),
            string.IsNullOrWhiteSpace(role) ? null : ValidateRole(role),
            enabled,
            page,
            pageSize,
            cancellationToken);
        var totalPages = result.TotalCount == 0
            ? 0
            : (int)Math.Ceiling(result.TotalCount / (double)pageSize);

        return new AdminUserPageDto(
            result.Items.Select(Map).ToArray(),
            page,
            pageSize,
            result.TotalCount,
            totalPages);
    }

    public async Task<AdminUserDto?> GetAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        var user = await keycloakAdminService.GetUserAsync(userId, cancellationToken);
        return user is null ? null : Map(user);
    }

    public async Task<AdminUserCreationDto> CreateAsync(
        CreateAdminUserCommand command,
        AdminUserActor actor,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actor);
        var normalizedCommand = ValidateCreate(command);
        IdentityUser? createdUser = null;

        try
        {
            createdUser = await keycloakAdminService.CreateUserAsync(normalizedCommand, cancellationToken);
            await WriteAuditAsync(
                actor,
                "USER_CREATED",
                createdUser.Id,
                newRole: createdUser.Role,
                newEnabled: createdUser.Enabled,
                succeeded: true,
                cancellationToken: cancellationToken);

            return new AdminUserCreationDto(Map(createdUser));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await WriteFailureAuditAsync(
                actor,
                "USER_CREATED",
                createdUser?.Id,
                exception,
                cancellationToken);
            throw;
        }
    }

    public async Task<AdminUserDto> UpdateAsync(
        string userId,
        UpdateAdminUserCommand command,
        AdminUserActor actor,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actor);
        ValidateUserId(userId);
        var normalizedCommand = ValidateUpdate(command);

        try
        {
            var previous = await GetRequiredUserAsync(userId, cancellationToken);
            var updated = await keycloakAdminService.UpdateUserAsync(
                userId,
                normalizedCommand,
                cancellationToken);
            await WriteAuditAsync(
                actor,
                "USER_UPDATED",
                userId,
                previous.Role,
                updated.Role,
                previous.Enabled,
                updated.Enabled,
                true,
                cancellationToken: cancellationToken);
            return Map(updated);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await WriteFailureAuditAsync(actor, "USER_UPDATED", userId, exception, cancellationToken);
            throw;
        }
    }

    public async Task<AdminUserDto> ChangeRoleAsync(
        string userId,
        string role,
        AdminUserActor actor,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actor);
        ValidateUserId(userId);
        var normalizedRole = ValidateRole(role);

        await AdminMutationLock.WaitAsync(cancellationToken);
        try
        {
            var previous = await GetRequiredUserAsync(userId, cancellationToken);

            if (string.Equals(actor.Subject, userId, StringComparison.Ordinal)
                && string.Equals(previous.Role, AdminRole, StringComparison.Ordinal)
                && !string.Equals(normalizedRole, AdminRole, StringComparison.Ordinal))
            {
                throw new AdminUserOperationNotAllowedException("You cannot remove your own administrator role.");
            }

            if (previous.Enabled
                && string.Equals(previous.Role, AdminRole, StringComparison.Ordinal)
                && !string.Equals(normalizedRole, AdminRole, StringComparison.Ordinal))
            {
                await EnsureAnotherActiveAdminExistsAsync(cancellationToken);
            }

            var updated = await keycloakAdminService.SetUserRoleAsync(
                userId,
                normalizedRole,
                cancellationToken);
            await WriteAuditAsync(
                actor,
                "USER_ROLE_CHANGED",
                userId,
                previous.Role,
                updated.Role,
                previous.Enabled,
                updated.Enabled,
                true,
                cancellationToken: cancellationToken);
            return Map(updated);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await WriteFailureAuditAsync(actor, "USER_ROLE_CHANGED", userId, exception, cancellationToken);
            throw;
        }
        finally
        {
            AdminMutationLock.Release();
        }
    }

    public async Task<AdminUserDto> ChangeStatusAsync(
        string userId,
        bool enabled,
        AdminUserActor actor,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actor);
        ValidateUserId(userId);

        await AdminMutationLock.WaitAsync(cancellationToken);
        try
        {
            var previous = await GetRequiredUserAsync(userId, cancellationToken);

            if (!enabled && string.Equals(actor.Subject, userId, StringComparison.Ordinal))
            {
                throw new AdminUserOperationNotAllowedException("You cannot disable your own account.");
            }

            if (!enabled
                && previous.Enabled
                && string.Equals(previous.Role, AdminRole, StringComparison.Ordinal))
            {
                await EnsureAnotherActiveAdminExistsAsync(cancellationToken);
            }

            var updated = await keycloakAdminService.SetUserEnabledAsync(
                userId,
                enabled,
                cancellationToken);
            await WriteAuditAsync(
                actor,
                enabled ? "USER_ENABLED" : "USER_DISABLED",
                userId,
                previous.Role,
                updated.Role,
                previous.Enabled,
                updated.Enabled,
                true,
                cancellationToken: cancellationToken);
            return Map(updated);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await WriteFailureAuditAsync(
                actor,
                enabled ? "USER_ENABLED" : "USER_DISABLED",
                userId,
                exception,
                cancellationToken);
            throw;
        }
        finally
        {
            AdminMutationLock.Release();
        }
    }

    public async Task<AdminUserDto> ChangeMfaRequirementAsync(
        string userId,
        bool required,
        AdminUserActor actor,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actor);
        ValidateUserId(userId);

        try
        {
            _ = await GetRequiredUserAsync(userId, cancellationToken);
            var updated = await keycloakAdminService.SetMfaRequiredAsync(
                userId,
                required,
                cancellationToken);
            await WriteAuditAsync(
                actor,
                "USER_MFA_REQUIREMENT_CHANGED",
                userId,
                succeeded: true,
                cancellationToken: cancellationToken);
            return Map(updated);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await WriteFailureAuditAsync(
                actor,
                "USER_MFA_REQUIREMENT_CHANGED",
                userId,
                exception,
                cancellationToken);
            throw;
        }
    }

    public async Task RequestPasswordResetAsync(
        string userId,
        AdminUserActor actor,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actor);
        ValidateUserId(userId);

        try
        {
            _ = await GetRequiredUserAsync(userId, cancellationToken);
            await keycloakAdminService.SendActionsEmailAsync(
                userId,
                ["UPDATE_PASSWORD"],
                cancellationToken);
            await WriteAuditAsync(
                actor,
                "USER_PASSWORD_RESET_REQUESTED",
                userId,
                succeeded: true,
                cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await WriteFailureAuditAsync(
                actor,
                "USER_PASSWORD_RESET_REQUESTED",
                userId,
                exception,
                cancellationToken);
            throw;
        }
    }

    public async Task LogoutAsync(
        string userId,
        AdminUserActor actor,
        CancellationToken cancellationToken = default)
    {
        ValidateActor(actor);
        ValidateUserId(userId);

        try
        {
            _ = await GetRequiredUserAsync(userId, cancellationToken);
            await keycloakAdminService.LogoutUserAsync(userId, cancellationToken);
            await WriteAuditAsync(
                actor,
                "USER_SESSIONS_LOGGED_OUT",
                userId,
                succeeded: true,
                cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await WriteFailureAuditAsync(
                actor,
                "USER_SESSIONS_LOGGED_OUT",
                userId,
                exception,
                cancellationToken);
            throw;
        }
    }

    private async Task<IdentityUser> GetRequiredUserAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        return await keycloakAdminService.GetUserAsync(userId, cancellationToken)
            ?? throw new IdentityUserNotFoundException(userId);
    }

    private async Task EnsureAnotherActiveAdminExistsAsync(CancellationToken cancellationToken)
    {
        var activeAdminCount = await keycloakAdminService.CountActiveUsersWithRoleAsync(
            AdminRole,
            cancellationToken);
        if (activeAdminCount <= 1)
        {
            throw new AdminUserOperationNotAllowedException(
                "The last active administrator cannot be disabled or have their administrator role removed.");
        }
    }

    private async Task WriteFailureAuditAsync(
        AdminUserActor actor,
        string action,
        string? targetUserId,
        Exception exception,
        CancellationToken cancellationToken)
    {
        await WriteAuditAsync(
            actor,
            action,
            targetUserId,
            succeeded: false,
            failureCode: GetFailureCode(exception),
            cancellationToken: cancellationToken);
    }

    private async Task WriteAuditAsync(
        AdminUserActor actor,
        string action,
        string? targetUserId,
        string? previousRole = null,
        string? newRole = null,
        bool? previousEnabled = null,
        bool? newEnabled = null,
        bool succeeded = false,
        string? failureCode = null,
        CancellationToken cancellationToken = default)
    {
        var audit = new UserManagementAudit
        {
            ActorSubject = actor.Subject,
            ActorUsername = NormalizeOptional(actor.Username),
            ActorEmail = NormalizeOptional(actor.Email),
            Action = action,
            TargetUserId = NormalizeOptional(targetUserId),
            PreviousRole = previousRole,
            NewRole = newRole,
            PreviousEnabled = previousEnabled,
            NewEnabled = newEnabled,
            Succeeded = succeeded,
            FailureCode = failureCode
        };

        await auditRepository.AddAsync(audit, cancellationToken);
        await auditRepository.SaveChangesAsync(cancellationToken);
    }

    private static CreateAdminUserCommand ValidateCreate(CreateAdminUserCommand command)
    {
        var username = command.Username.Trim();
        if (username.Length is < UsernameMinLength or > UsernameMaxLength)
        {
            throw new InvalidOperationException(
                $"Kullanıcı adı {UsernameMinLength} ile {UsernameMaxLength} karakter arasında olmalıdır.");
        }

        if (string.IsNullOrEmpty(command.TemporaryPassword)
            || command.TemporaryPassword.Length is < TemporaryPasswordMinLength
                or > TemporaryPasswordMaxLength)
        {
            throw new InvalidOperationException(
                $"Geçici parola {TemporaryPasswordMinLength} ile {TemporaryPasswordMaxLength} karakter arasında olmalıdır.");
        }

        var email = ValidateOptionalEmail(command.Email);
        return command with
        {
            Username = username,
            FirstName = ValidateName(command.FirstName, "Ad"),
            LastName = ValidateName(command.LastName, "Soyad"),
            Email = email,
            Role = ValidateRole(command.Role)
        };
    }

    private static UpdateAdminUserCommand ValidateUpdate(UpdateAdminUserCommand command) =>
        command with
        {
            FirstName = ValidateName(command.FirstName, "Ad"),
            LastName = ValidateName(command.LastName, "Soyad"),
            Email = ValidateOptionalEmail(command.Email)
        };

    private static string? ValidateName(string? value, string label)
    {
        var normalized = NormalizeOptional(value);
        if (normalized?.Length > 128)
        {
            throw new InvalidOperationException($"{label} 128 karakteri aşamaz.");
        }

        return normalized;
    }

    private static string? ValidateOptionalEmail(string? value)
    {
        var email = NormalizeOptional(value);
        if (email is null)
        {
            return null;
        }

        if (email.Length > 320 || !MailAddress.TryCreate(email, out var address)
            || !string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Geçerli bir e-posta adresi gereklidir.");
        }

        return email;
    }

    private static string ValidateRole(string role)
    {
        var normalized = role.Trim();
        if (!ManagedRoles.Contains(normalized))
        {
            throw new InvalidOperationException("Desteklenmeyen DBScanner rolü.");
        }

        return normalized;
    }

    private static void ValidateActor(AdminUserActor actor)
    {
        if (string.IsNullOrWhiteSpace(actor.Subject))
        {
            throw new InvalidOperationException("Authenticated user subject is required.");
        }
    }

    private static void ValidateUserId(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128)
        {
            throw new InvalidOperationException("A valid identity user id is required.");
        }
    }

    private static string GetFailureCode(Exception exception) => exception switch
    {
        IdentityManagementUnavailableException => "IDENTITY_SERVICE_UNAVAILABLE",
        IdentityEmailDeliveryException => "IDENTITY_EMAIL_DELIVERY_FAILED",
        IdentityManagementConflictException => "IDENTITY_CONFLICT",
        IdentityUserNotFoundException => "IDENTITY_USER_NOT_FOUND",
        AdminUserOperationNotAllowedException => "OPERATION_NOT_ALLOWED",
        _ => "VALIDATION_OR_OPERATION_FAILED"
    };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AdminUserDto Map(IdentityUser user) => new(
        user.Id,
        user.Username,
        user.FirstName,
        user.LastName,
        user.Email,
        user.Role,
        user.Enabled,
        user.EmailVerified,
        user.MfaConfigured,
        user.MfaRequired,
        user.LastLoginAt);
}
