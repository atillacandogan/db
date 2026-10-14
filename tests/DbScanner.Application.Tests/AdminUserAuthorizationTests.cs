using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DbScanner.Application.Exceptions;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Contracts.AdminUsers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DbScanner.Application.Tests;

public sealed class AdminUserAuthorizationTests
{
    [Theory]
    [InlineData("dbscanner-viewer")]
    [InlineData("dbscanner-operator")]
    public async Task NonAdminCannotListAdminUsers(string role)
    {
        await using var factory = CreateFactory(failWithUnavailable: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);

        using var response = await client.GetAsync("/api/admin/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminCanListAdminUsers()
    {
        await using var factory = CreateFactory(failWithUnavailable: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "dbscanner-admin");

        using var response = await client.GetAsync("/api/admin/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("dbscanner-viewer")]
    [InlineData("dbscanner-operator")]
    public async Task NonAdminCannotReadScanCompatibilityReadiness(string role)
    {
        await using var factory = CreateFactory(failWithUnavailable: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);

        using var response = await client.GetAsync("/api/scan-checks/compatibility-readiness");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminCanReadScanCompatibilityReadiness()
    {
        await using var factory = CreateFactory(failWithUnavailable: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "dbscanner-admin");

        using var response = await client.GetAsync("/api/scan-checks/compatibility-readiness");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"readyForLegacyRemoval\":true", body);
    }

    [Theory]
    [InlineData("dbscanner-viewer")]
    [InlineData("dbscanner-operator")]
    public async Task NonAdminCannotReadExecutorCapabilities(string role)
    {
        await using var factory = CreateFactory(failWithUnavailable: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);

        using var response = await client.GetAsync("/api/scan-checks/executors");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminCanReadExecutorCapabilities()
    {
        await using var factory = CreateFactory(failWithUnavailable: false);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "dbscanner-admin");

        using var response = await client.GetAsync("/api/scan-checks/executors");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"executorType\":\"nmap-nse\"", body);
        Assert.Contains("\"allowsManualCheckCreation\":true", body);
    }

    [Fact]
    public async Task IdentityServiceFailureReturnsServiceUnavailableWithoutRawDetails()
    {
        await using var factory = CreateFactory(failWithUnavailable: true);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "dbscanner-admin");

        using var response = await client.GetAsync("/api/admin/users");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Kimlik yönetimi servisine şu anda ulaşılamıyor.", body);
        Assert.DoesNotContain("raw-keycloak-payload", body);
    }

    private static WebApplicationFactory<Program> CreateFactory(bool failWithUnavailable) =>
        new TestWebApplicationFactory(failWithUnavailable);

    private sealed class TestWebApplicationFactory(bool failWithUnavailable)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Set before the minimal Program registers infrastructure; this
            // fixture never contacts a database (NoDatabaseVerifier below).
            builder.UseSetting("ConnectionStrings:dbscanner", "Host=localhost;Database=authorization-test;Username=test");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Persistence:ApplyMigrations"] = "false",
                    ["Authentication:Authority"] = "http://identity.invalid/realms/dbscanner",
                    ["Authentication:Audience"] = "dbscanner-api",
                    ["Authentication:RequireHttpsMetadata"] = "false"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<DbScanner.Infrastructure.Identity.ITenantDeploymentVerifier, NoDatabaseVerifier>();
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = "Test";
                        options.DefaultChallengeScheme = "Test";
                        options.DefaultForbidScheme = "Test";
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                services.AddScoped<IAdminUserService>(_ =>
                    new FakeAdminUserService(failWithUnavailable));
                services.AddScoped<IScanCompatibilityReadinessService, FakeScanCompatibilityReadinessService>();
                services.AddSingleton<IScanExecutorStrategyResolver, FakeScanExecutorStrategyResolver>();
            });
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim("sub", "test-user-id"),
                new Claim("preferred_username", "test-user"),
                new Claim("roles", role.ToString())
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name, null, "roles"));
            return Task.FromResult(
                AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }

    private sealed class NoDatabaseVerifier : DbScanner.Infrastructure.Identity.ITenantDeploymentVerifier
    {
        public Task VerifyAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task VerifyBeforeMigrationAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeAdminUserService(bool failWithUnavailable) : IAdminUserService
    {
        public Task<AdminUserPageDto> ListAsync(
            string? search,
            string? role,
            bool? enabled,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            if (failWithUnavailable)
            {
                throw new IdentityManagementUnavailableException("raw-keycloak-payload");
            }

            return Task.FromResult(new AdminUserPageDto([], page, pageSize, 0, 0));
        }

        public Task<AdminUserDto?> GetAsync(string userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdminUserCreationDto> CreateAsync(
            CreateAdminUserCommand command,
            AdminUserActor actor,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AdminUserDto> UpdateAsync(
            string userId,
            UpdateAdminUserCommand command,
            AdminUserActor actor,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AdminUserDto> ChangeRoleAsync(
            string userId,
            string role,
            AdminUserActor actor,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AdminUserDto> ChangeStatusAsync(
            string userId,
            bool enabled,
            AdminUserActor actor,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AdminUserDto> ChangeMfaRequirementAsync(
            string userId,
            bool required,
            AdminUserActor actor,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RequestPasswordResetAsync(
            string userId,
            AdminUserActor actor,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task LogoutAsync(
            string userId,
            AdminUserActor actor,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeScanCompatibilityReadinessService : IScanCompatibilityReadinessService
    {
        public Task<ScanCompatibilityReadiness> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScanCompatibilityReadiness(2, 0, 4, 0, 0, 8, 0, 0));
    }

    private sealed class FakeScanExecutorStrategyResolver : IScanExecutorStrategyResolver
    {
        private static readonly ScanExecutorCapability Capability = new(
            ScanExecutorTypes.NmapNse,
            "Nmap NSE",
            "Test executor.",
            Enum.GetValues<DbScanner.Domain.Enums.DatabaseEngine>(),
            AllowsManualCheckCreation: true,
            AllowsCheckNameEditing: false,
            SupportsConfigurationSchema: true);

        public IReadOnlyCollection<string> RegisteredExecutorTypes => [Capability.ExecutorType];
        public IReadOnlyCollection<ScanExecutorCapability> RegisteredCapabilities => [Capability];

        public IScanExecutorStrategy Resolve(string executorType) =>
            throw new NotSupportedException();
    }
}
