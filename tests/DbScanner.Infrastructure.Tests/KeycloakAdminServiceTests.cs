using System.Collections.Concurrent;
using System.Net;
using System.Text;
using DbScanner.Application.Models;
using DbScanner.Application.Exceptions;
using DbScanner.Infrastructure.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

public sealed class KeycloakAdminServiceTests
{
    [Fact]
    public async Task UserDirectoryExcludesServiceAccountsAndPreservesRealProviderId()
    {
        var handler = new KeycloakStubHandler(includeServiceAccount: true);
        var service = CreateService(handler, "test-client-secret");
        var page = await service.ListUsersAsync("ada", null, true, 1, 10);
        Assert.Equal("user-1", Assert.Single(page.Items).Id);
        Assert.DoesNotContain(handler.Requests, request => request.Path.Contains("service-user/credentials", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ServiceAccountAndMissingIdCannotBeResolvedAsTeamUsers()
    {
        var handler = new KeycloakStubHandler();
        var service = CreateService(handler, "test-client-secret");
        Assert.Null(await service.GetUserAsync("service-user"));
        Assert.Null(await service.GetUserAsync("missing-user"));
        Assert.DoesNotContain(handler.Requests, request => request.Path.Contains("service-user/role-mappings", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListUsersAuthenticatesAndEnrichesRoleAndMfaState()
    {
        var handler = new KeycloakStubHandler();
        var service = CreateService(handler, clientSecret: "test-client-secret");

        var page = await service.ListUsersAsync(
            search: "ada",
            role: null,
            enabled: true,
            page: 1,
            pageSize: 20);

        var user = Assert.Single(page.Items);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal("ada", user.Username);
        Assert.Equal("dbscanner-admin", user.Role);
        Assert.True(user.Enabled);
        Assert.True(user.EmailVerified);
        Assert.True(user.MfaConfigured);

        var requests = handler.Requests.ToArray();
        var tokenRequest = Assert.Single(requests, request =>
            request.Path == "/realms/dbscanner/protocol/openid-connect/token");
        Assert.Equal(HttpMethod.Post, tokenRequest.Method);
        Assert.Contains("grant_type=client_credentials", tokenRequest.Body);
        Assert.Contains("client_id=dbscanner-admin-service", tokenRequest.Body);

        Assert.Contains(requests, request =>
            request.Path.Contains("search=ada", StringComparison.Ordinal)
            && request.Path.Contains("enabled=true", StringComparison.Ordinal));
        Assert.All(
            requests.Where(request => request != tokenRequest),
            request => Assert.Equal("Bearer test-access-token", request.Authorization));
    }

    [Fact]
    public async Task CreateUserSetsTemporaryPasswordWithoutEmailVerificationAction()
    {
        var handler = new KeycloakStubHandler();
        var service = CreateService(handler, clientSecret: "test-client-secret");

        var user = await service.CreateUserAsync(
            new CreateAdminUserCommand(
                "new-user",
                "New",
                "User",
                null,
                "dbscanner-admin",
                true,
                "Temporary!123"));

        Assert.Equal("new-user", user.Username);
        Assert.Equal("dbscanner-admin", user.Role);

        var createRequest = Assert.Single(handler.Requests, request =>
            request.Method == HttpMethod.Post
            && request.Path == "/admin/realms/dbscanner/users");
        Assert.Contains("\"value\":\"Temporary!123\"", createRequest.Body);
        Assert.Contains("\"temporary\":true", createRequest.Body);
        Assert.Contains("\"UPDATE_PASSWORD\"", createRequest.Body);
        Assert.Contains("\"CONFIGURE_TOTP\"", createRequest.Body);
        Assert.DoesNotContain("\"email\"", createRequest.Body);
        Assert.DoesNotContain("VERIFY_EMAIL", createRequest.Body);
        Assert.DoesNotContain(handler.Requests, request =>
            request.Path.Contains("execute-actions-email", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingClientSecretFailsBeforeCallingIdentityProvider()
    {
        var handler = new KeycloakStubHandler();
        var service = CreateService(handler, clientSecret: null);

        var exception = await Assert.ThrowsAsync<IdentityManagementUnavailableException>(() =>
            service.ListUsersAsync(null, null, null, 1, 20));

        Assert.Equal("Keycloak admin service account is not configured.", exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task RejectedServiceAccountCredentialsReturnUnavailableError()
    {
        var handler = new KeycloakStubHandler(rejectToken: true);
        var service = CreateService(handler, clientSecret: "invalid-test-secret");

        var exception = await Assert.ThrowsAsync<IdentityManagementUnavailableException>(() =>
            service.ListUsersAsync(null, null, null, 1, 20));

        Assert.Equal("Keycloak admin service account could not authenticate.", exception.Message);
    }

    private static KeycloakAdminService CreateService(
        HttpMessageHandler handler,
        string? clientSecret)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://identity.test/")
        };
        var options = Options.Create(new KeycloakAdminOptions
        {
            BaseUrl = "http://identity.test",
            Realm = "dbscanner",
            ClientId = "dbscanner-admin-service",
            ClientSecret = clientSecret
        });
        return new KeycloakAdminService(
            httpClient,
            options,
            NullLogger<KeycloakAdminService>.Instance);
    }

    private sealed class KeycloakStubHandler(bool rejectToken = false, bool includeServiceAccount = false) : HttpMessageHandler
    {
        public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri?.PathAndQuery ?? string.Empty;
            Requests.Enqueue(new RecordedRequest(
                request.Method,
                path,
                request.Headers.Authorization?.ToString(),
                body));

            if (path == "/realms/dbscanner/protocol/openid-connect/token")
            {
                return rejectToken
                    ? Json(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_client\"}")
                    : Json(HttpStatusCode.OK, "{\"access_token\":\"test-access-token\",\"expires_in\":300}");
            }

            if (path.StartsWith("/admin/realms/dbscanner/users?", StringComparison.Ordinal))
            {
                if (includeServiceAccount)
                    return Json(HttpStatusCode.OK, """
                        [{"id":"user-1","username":"ada","enabled":true},
                         {"id":"service-user","username":"service-account-admin","enabled":true,"serviceAccountClientId":"admin"}]
                        """);
                return Json(
                    HttpStatusCode.OK,
                    """
                    [{
                      "id": "user-1",
                      "username": "ada",
                      "firstName": "Ada",
                      "lastName": "Lovelace",
                      "email": "ada@example.test",
                      "enabled": true,
                      "emailVerified": true,
                      "requiredActions": []
                    }]
                    """);
            }

            if (path == "/admin/realms/dbscanner/users/service-user")
                return Json(HttpStatusCode.OK, """
                    {"id":"service-user","username":"service-account-admin","enabled":true,"serviceAccountClientId":"admin"}
                    """);

            if (path == "/admin/realms/dbscanner/users" && request.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Headers =
                    {
                        Location = new Uri(
                            "http://identity.test/admin/realms/dbscanner/users/new-user-id")
                    }
                };
            }

            if (path == "/admin/realms/dbscanner/users/new-user-id")
            {
                return Json(
                    HttpStatusCode.OK,
                    """
                    {
                      "id": "new-user-id",
                      "username": "new-user",
                      "firstName": "New",
                      "lastName": "User",
                      "email": "new-user@example.test",
                      "enabled": true,
                      "emailVerified": false,
                      "requiredActions": ["UPDATE_PASSWORD", "CONFIGURE_TOTP"]
                    }
                    """);
            }

            if (path == "/admin/realms/dbscanner/users/new-user-id/role-mappings/realm"
                && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, "[]");
            }

            if (path == "/admin/realms/dbscanner/roles/dbscanner-admin")
            {
                return Json(
                    HttpStatusCode.OK,
                    "{\"id\":\"role-1\",\"name\":\"dbscanner-admin\",\"clientRole\":false}");
            }

            if (path == "/admin/realms/dbscanner/users/new-user-id/role-mappings/realm"
                && request.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (path == "/admin/realms/dbscanner/users/new-user-id/role-mappings/realm/composite")
            {
                return Json(HttpStatusCode.OK, "[{\"id\":\"role-1\",\"name\":\"dbscanner-admin\"}]");
            }

            if (path == "/admin/realms/dbscanner/users/new-user-id/credentials")
            {
                return Json(HttpStatusCode.OK, "[{\"type\":\"password\"}]");
            }

            if (path == "/admin/realms/dbscanner/users/count?search=ada&enabled=true")
            {
                return Json(HttpStatusCode.OK, "1");
            }

            if (path == "/admin/realms/dbscanner/users/user-1/role-mappings/realm/composite")
            {
                return Json(HttpStatusCode.OK, "[{\"id\":\"role-1\",\"name\":\"dbscanner-admin\"}]");
            }

            if (path == "/admin/realms/dbscanner/users/user-1/credentials")
            {
                return Json(HttpStatusCode.OK, "[{\"type\":\"otp\"}]");
            }

            return Json(HttpStatusCode.NotFound, "{}");
        }

        private static HttpResponseMessage Json(HttpStatusCode statusCode, string body) =>
            new(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Path,
        string? Authorization,
        string? Body);
}
