using DbScanner.Application.Exceptions;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Application.Services;
using DbScanner.Domain.Entities;
using Xunit;

namespace DbScanner.Application.Tests;

public sealed class AdminUserServiceTests
{
    private static readonly AdminUserActor Actor = new("actor-id", "admin", "admin@example.com");

    [Fact]
    public async Task ChangeRoleRejectsRolesOutsideDbScannerWhitelist()
    {
        var (service, _, _) = CreateService(User(role: AdminUserService.AdminRole));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ChangeRoleAsync("user-id", "realm-admin", Actor));

        Assert.Equal("Desteklenmeyen DBScanner rolü.", exception.Message);
    }

    [Fact]
    public async Task AdminCannotDisableOwnAccount()
    {
        var self = User(id: "actor-id", role: AdminUserService.AdminRole);
        var (service, _, audit) = CreateService(self);

        await Assert.ThrowsAsync<AdminUserOperationNotAllowedException>(() =>
            service.ChangeStatusAsync(self.Id, false, Actor));

        var entry = Assert.Single(audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal("USER_DISABLED", entry.Action);
        Assert.Equal("OPERATION_NOT_ALLOWED", entry.FailureCode);
    }

    [Fact]
    public async Task AdminCannotRemoveOwnAdminRole()
    {
        var self = User(id: "actor-id", role: AdminUserService.AdminRole);
        var (service, _, audit) = CreateService(self);

        await Assert.ThrowsAsync<AdminUserOperationNotAllowedException>(() =>
            service.ChangeRoleAsync(self.Id, AdminUserService.OperatorRole, Actor));

        Assert.Equal("USER_ROLE_CHANGED", Assert.Single(audit.Entries).Action);
    }

    [Fact]
    public async Task LastActiveAdminCannotBeDisabled()
    {
        var admin = User(role: AdminUserService.AdminRole);
        var (service, keycloak, _) = CreateService(admin);
        keycloak.ActiveAdminCount = 1;

        await Assert.ThrowsAsync<AdminUserOperationNotAllowedException>(() =>
            service.ChangeStatusAsync(admin.Id, false, Actor));

        Assert.Equal(0, keycloak.StatusUpdateCount);
    }

    [Fact]
    public async Task LastActiveAdminCannotBeDowngraded()
    {
        var admin = User(role: AdminUserService.AdminRole);
        var (service, keycloak, _) = CreateService(admin);
        keycloak.ActiveAdminCount = 1;

        await Assert.ThrowsAsync<AdminUserOperationNotAllowedException>(() =>
            service.ChangeRoleAsync(admin.Id, AdminUserService.ViewerRole, Actor));

        Assert.Equal(0, keycloak.RoleUpdateCount);
    }

    [Fact]
    public async Task SuccessfulRoleChangeWritesSanitizedAuditEntry()
    {
        var admin = User(role: AdminUserService.AdminRole);
        var (service, keycloak, audit) = CreateService(admin);
        keycloak.ActiveAdminCount = 2;

        var result = await service.ChangeRoleAsync(
            admin.Id,
            AdminUserService.OperatorRole,
            Actor);

        Assert.Equal(AdminUserService.OperatorRole, result.Role);
        var entry = Assert.Single(audit.Entries);
        Assert.True(entry.Succeeded);
        Assert.Equal("USER_ROLE_CHANGED", entry.Action);
        Assert.Equal(AdminUserService.AdminRole, entry.PreviousRole);
        Assert.Equal(AdminUserService.OperatorRole, entry.NewRole);
        Assert.Null(entry.FailureCode);
        Assert.DoesNotContain("token", string.Join('|', AuditValues(entry)), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", string.Join('|', AuditValues(entry)), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", string.Join('|', AuditValues(entry)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuditSchemaCannotStorePasswordsTokensOrClientSecrets()
    {
        var propertyNames = typeof(UserManagementAudit)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(propertyNames, name =>
            name.Contains("password", StringComparison.OrdinalIgnoreCase)
            || name.Contains("token", StringComparison.OrdinalIgnoreCase)
            || name.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateCommandStringRepresentationRedactsTemporaryPassword()
    {
        var command = new CreateAdminUserCommand(
            "new-user",
            "New",
            "User",
            "new-user@example.com",
            AdminUserService.ViewerRole,
            false,
            "Temporary!123");

        Assert.DoesNotContain("Temporary!123", command.ToString(), StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", command.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateRejectsUsernameShorterThanKeycloakMinimum()
    {
        var (service, _, _) = CreateService(User());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(
                new CreateAdminUserCommand(
                    "ac",
                    "A",
                    "C",
                    "ac@example.com",
                    AdminUserService.AdminRole,
                    false,
                    "Temporary!123"),
                Actor));

        Assert.Equal("Kullanıcı adı 3 ile 128 karakter arasında olmalıdır.", exception.Message);
    }

    [Fact]
    public async Task CreateSucceedsWithoutSendingEmailAndWritesAuditEntry()
    {
        var user = User();
        var (service, keycloak, audit) = CreateService(user);
        var result = await service.CreateAsync(
            new CreateAdminUserCommand(
                "new-user",
                "New",
                "User",
                null,
                AdminUserService.ViewerRole,
                false,
                "Temporary!123"),
            Actor);

        Assert.Equal(user.Id, result.User.Id);
        Assert.Equal(0, keycloak.EmailDeliveryCount);
        var entry = Assert.Single(audit.Entries);
        Assert.True(entry.Succeeded);
        Assert.Equal("USER_CREATED", entry.Action);
    }

    [Fact]
    public async Task CreateRejectsWeakTemporaryPassword()
    {
        var (service, _, _) = CreateService(User());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(
                new CreateAdminUserCommand(
                    "new-user",
                    "New",
                    "User",
                    "new-user@example.com",
                    AdminUserService.ViewerRole,
                    false,
                    "short"),
                Actor));

        Assert.Equal("Geçici parola 12 ile 128 karakter arasında olmalıdır.", exception.Message);
    }

    private static IEnumerable<string> AuditValues(UserManagementAudit audit)
    {
        yield return audit.ActorSubject;
        yield return audit.ActorUsername ?? string.Empty;
        yield return audit.ActorEmail ?? string.Empty;
        yield return audit.Action;
        yield return audit.TargetUserId ?? string.Empty;
        yield return audit.PreviousRole ?? string.Empty;
        yield return audit.NewRole ?? string.Empty;
        yield return audit.FailureCode ?? string.Empty;
    }

    private static (AdminUserService Service, FakeKeycloakAdminService Keycloak, FakeAuditRepository Audit)
        CreateService(IdentityUser user)
    {
        var keycloak = new FakeKeycloakAdminService(user);
        var audit = new FakeAuditRepository();
        return (new AdminUserService(keycloak, audit), keycloak, audit);
    }

    private static IdentityUser User(
        string id = "user-id",
        string role = AdminUserService.ViewerRole,
        bool enabled = true) =>
        new(
            id,
            "user",
            "Test",
            "User",
            "user@example.com",
            role,
            enabled,
            true,
            false,
            false,
            null);

    private sealed class FakeAuditRepository : IUserManagementAuditRepository
    {
        public List<UserManagementAudit> Entries { get; } = [];

        public Task AddAsync(UserManagementAudit audit, CancellationToken cancellationToken = default)
        {
            Entries.Add(audit);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeKeycloakAdminService(IdentityUser user) : IKeycloakAdminService
    {
        private IdentityUser _user = user;

        public int ActiveAdminCount { get; set; } = 2;
        public int RoleUpdateCount { get; private set; }
        public int StatusUpdateCount { get; private set; }
        public int EmailDeliveryCount { get; private set; }

        public Task<IdentityUserPage> ListUsersAsync(
            string? search,
            string? role,
            bool? enabled,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new IdentityUserPage([_user], 1));

        public Task<IdentityUser?> GetUserAsync(
            string userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IdentityUser?>(userId == _user.Id ? _user : null);

        public Task<IdentityUser> CreateUserAsync(
            CreateAdminUserCommand command,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_user);

        public Task<IdentityUser> UpdateUserAsync(
            string userId,
            UpdateAdminUserCommand command,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_user);

        public Task<IdentityUser> SetUserRoleAsync(
            string userId,
            string role,
            CancellationToken cancellationToken = default)
        {
            RoleUpdateCount++;
            _user = _user with { Role = role };
            return Task.FromResult(_user);
        }

        public Task<IdentityUser> SetUserEnabledAsync(
            string userId,
            bool enabled,
            CancellationToken cancellationToken = default)
        {
            StatusUpdateCount++;
            _user = _user with { Enabled = enabled };
            return Task.FromResult(_user);
        }

        public Task<IdentityUser> SetMfaRequiredAsync(
            string userId,
            bool required,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_user with { MfaRequired = required });

        public Task<int> CountActiveUsersWithRoleAsync(
            string role,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ActiveAdminCount);

        public Task SendActionsEmailAsync(
            string userId,
            IReadOnlyCollection<string> actions,
            CancellationToken cancellationToken = default)
        {
            EmailDeliveryCount++;
            return Task.CompletedTask;
        }

        public Task LogoutUserAsync(
            string userId,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
