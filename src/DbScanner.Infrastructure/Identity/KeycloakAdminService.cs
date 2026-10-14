using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DbScanner.Application.Exceptions;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DbScanner.Infrastructure.Identity;

public sealed class KeycloakAdminService(
    HttpClient httpClient,
    IOptions<KeycloakAdminOptions> options,
    ILogger<KeycloakAdminService> logger) : IKeycloakAdminService
{
    private const string ConfigureTotpAction = "CONFIGURE_TOTP";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly KeycloakAdminOptions _options = options.Value;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt;

    public async Task<IdentityUserPage> ListUsersAsync(
        string? search,
        string? role,
        bool? enabled,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(role))
        {
            return await ListUsersFilteredByRoleAsync(
                search,
                role,
                enabled,
                page,
                pageSize,
                cancellationToken);
        }

        var first = checked((page - 1) * pageSize);
        var listPath = AdminPath(BuildUsersQuery("users", first, pageSize, search, enabled));
        var countPath = AdminPath(BuildUserCountQuery(search, enabled));

        using var listResponse = await SendAdminAsync(HttpMethod.Get, listPath, null, cancellationToken);
        EnsureSuccess(listResponse, "list users");
        var users = await DeserializeAsync<List<KeycloakUserRepresentation>>(
            listResponse,
            cancellationToken) ?? [];

        using var countResponse = await SendAdminAsync(HttpMethod.Get, countPath, null, cancellationToken);
        EnsureSuccess(countResponse, "count users");
        var totalCount = await DeserializeAsync<int>(countResponse, cancellationToken);
        var enrichedUsers = await Task.WhenAll(
            users.Where(user => !string.IsNullOrWhiteSpace(user.Id)
                    && string.IsNullOrWhiteSpace(user.ServiceAccountClientId))
                .Select(user => EnrichUserAsync(user, cancellationToken)));

        return new IdentityUserPage(enrichedUsers, totalCount);
    }

    private async Task<IdentityUserPage> ListUsersFilteredByRoleAsync(
        string? search,
        string role,
        bool? enabled,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        const int batchSize = 100;
        var first = 0;
        var matches = new List<IdentityUser>();

        while (true)
        {
            using var response = await SendAdminAsync(
                HttpMethod.Get,
                AdminPath(BuildUsersQuery("users", first, batchSize, search, enabled)),
                null,
                cancellationToken);
            EnsureSuccess(response, "list filtered users");
            var users = await DeserializeAsync<List<KeycloakUserRepresentation>>(
                response,
                cancellationToken) ?? [];
            var enriched = await Task.WhenAll(
                users.Where(user => !string.IsNullOrWhiteSpace(user.Id)
                        && string.IsNullOrWhiteSpace(user.ServiceAccountClientId))
                    .Select(user => EnrichUserAsync(user, cancellationToken)));
            matches.AddRange(enriched.Where(user =>
                string.Equals(user.Role, role, StringComparison.Ordinal)));

            if (users.Count < batchSize)
            {
                break;
            }

            first += batchSize;
        }

        var offset = checked((page - 1) * pageSize);
        return new IdentityUserPage(
            matches.Skip(offset).Take(pageSize).ToArray(),
            matches.Count);
    }

    public async Task<IdentityUser?> GetUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var representation = await GetUserRepresentationAsync(userId, cancellationToken);
        return representation is null || !string.IsNullOrWhiteSpace(representation.ServiceAccountClientId)
            ? null
            : await EnrichUserAsync(representation, cancellationToken);
    }

    public async Task<IdentityUser> CreateUserAsync(
        CreateAdminUserCommand command,
        CancellationToken cancellationToken = default)
    {
        var requiredActions = new List<string> { "UPDATE_PASSWORD" };
        if (command.RequireMfa)
        {
            requiredActions.Add(ConfigureTotpAction);
        }

        var representation = new KeycloakUserRepresentation
        {
            Username = command.Username,
            FirstName = command.FirstName,
            LastName = command.LastName,
            Email = command.Email,
            Enabled = true,
            EmailVerified = false,
            RequiredActions = requiredActions,
            Credentials =
            [
                new KeycloakCredentialRepresentation
                {
                    Type = "password",
                    Value = command.TemporaryPassword,
                    Temporary = true
                }
            ]
        };

        using var response = await SendAdminAsync(
            HttpMethod.Post,
            AdminPath("users"),
            representation,
            cancellationToken);
        EnsureSuccess(response, "create user");

        var userId = response.Headers.Location?.Segments.LastOrDefault()?.Trim('/');
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new IdentityManagementUnavailableException(
                "Identity provider did not return the created user identifier.");
        }

        try
        {
            return await SetUserRoleAsync(userId, command.Role, cancellationToken);
        }
        catch
        {
            await TryDeleteCreatedUserAsync(userId, cancellationToken);
            throw;
        }
    }

    public async Task<IdentityUser> UpdateUserAsync(
        string userId,
        UpdateAdminUserCommand command,
        CancellationToken cancellationToken = default)
    {
        var representation = await GetRequiredUserRepresentationAsync(userId, cancellationToken);
        representation.FirstName = command.FirstName;
        representation.LastName = command.LastName;
        if (command.Email is not null
            && !string.Equals(representation.Email, command.Email, StringComparison.OrdinalIgnoreCase))
        {
            representation.Email = command.Email;
            representation.EmailVerified = false;
            representation.RequiredActions = AddRequiredAction(
                representation.RequiredActions,
                "VERIFY_EMAIL");
        }

        await PutUserRepresentationAsync(userId, representation, cancellationToken);
        return await GetRequiredUserAsync(userId, cancellationToken);
    }

    public async Task<IdentityUser> SetUserRoleAsync(
        string userId,
        string role,
        CancellationToken cancellationToken = default)
    {
        _ = await GetRequiredUserRepresentationAsync(userId, cancellationToken);
        var directRoles = await GetRolesAsync(
            AdminPath($"users/{Escape(userId)}/role-mappings/realm"),
            cancellationToken);
        var desiredRole = await GetRoleAsync(role, cancellationToken);

        if (!directRoles.Any(item => string.Equals(item.Name, role, StringComparison.Ordinal)))
        {
            await SendRoleMappingAsync(
                HttpMethod.Post,
                userId,
                [desiredRole],
                cancellationToken);
        }

        var rolesToRemove = directRoles
            .Where(item => IsManagedRole(item.Name)
                && !string.Equals(item.Name, role, StringComparison.Ordinal))
            .ToArray();
        if (rolesToRemove.Length > 0)
        {
            await SendRoleMappingAsync(
                HttpMethod.Delete,
                userId,
                rolesToRemove,
                cancellationToken);
        }

        return await GetRequiredUserAsync(userId, cancellationToken);
    }

    public async Task<IdentityUser> SetUserEnabledAsync(
        string userId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var representation = await GetRequiredUserRepresentationAsync(userId, cancellationToken);
        representation.Enabled = enabled;
        await PutUserRepresentationAsync(userId, representation, cancellationToken);
        return await GetRequiredUserAsync(userId, cancellationToken);
    }

    public async Task<IdentityUser> SetMfaRequiredAsync(
        string userId,
        bool required,
        CancellationToken cancellationToken = default)
    {
        var representation = await GetRequiredUserRepresentationAsync(userId, cancellationToken);
        var credentials = await GetCredentialTypesAsync(userId, cancellationToken);
        var mfaConfigured = credentials.Contains("otp", StringComparer.OrdinalIgnoreCase);
        representation.RequiredActions = required && !mfaConfigured
            ? AddRequiredAction(representation.RequiredActions, ConfigureTotpAction)
            : RemoveRequiredAction(representation.RequiredActions, ConfigureTotpAction);
        await PutUserRepresentationAsync(userId, representation, cancellationToken);
        return await GetRequiredUserAsync(userId, cancellationToken);
    }

    public async Task<int> CountActiveUsersWithRoleAsync(
        string role,
        CancellationToken cancellationToken = default)
    {
        const int batchSize = 100;
        var first = 0;
        var activeCount = 0;

        while (true)
        {
            var path = AdminPath(
                $"roles/{Escape(role)}/users?first={first}&max={batchSize}&briefRepresentation=true");
            using var response = await SendAdminAsync(HttpMethod.Get, path, null, cancellationToken);
            EnsureSuccess(response, "list role members");
            var users = await DeserializeAsync<List<KeycloakUserRepresentation>>(
                response,
                cancellationToken) ?? [];
            activeCount += users.Count(user => user.Enabled is true);

            if (users.Count < batchSize)
            {
                return activeCount;
            }

            first += batchSize;
        }
    }

    public async Task SendActionsEmailAsync(
        string userId,
        IReadOnlyCollection<string> actions,
        CancellationToken cancellationToken = default)
    {
        var query = $"client_id={Uri.EscapeDataString(_options.WebClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(_options.WebRedirectUri)}"
            + $"&lifespan={_options.EmailActionLifespanSeconds}";
        using var response = await SendAdminAsync(
            HttpMethod.Put,
            AdminPath($"users/{Escape(userId)}/execute-actions-email?{query}"),
            actions,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new IdentityUserNotFoundException(userId);
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Keycloak action email request failed with status {StatusCode}.",
                (int)response.StatusCode);
            throw new IdentityEmailDeliveryException(
                "Keycloak could not send the requested action email.");
        }
    }

    public async Task LogoutUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAdminAsync(
            HttpMethod.Post,
            AdminPath($"users/{Escape(userId)}/logout"),
            null,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new IdentityUserNotFoundException(userId);
        }

        EnsureSuccess(response, "logout user sessions");
    }

    private async Task<IdentityUser> GetRequiredUserAsync(
        string userId,
        CancellationToken cancellationToken) =>
        await GetUserAsync(userId, cancellationToken)
            ?? throw new IdentityUserNotFoundException(userId);

    private async Task<KeycloakUserRepresentation> GetRequiredUserRepresentationAsync(
        string userId,
        CancellationToken cancellationToken) =>
        await GetUserRepresentationAsync(userId, cancellationToken)
            ?? throw new IdentityUserNotFoundException(userId);

    private async Task<KeycloakUserRepresentation?> GetUserRepresentationAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        using var response = await SendAdminAsync(
            HttpMethod.Get,
            AdminPath($"users/{Escape(userId)}"),
            null,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureSuccess(response, "get user");
        return await DeserializeAsync<KeycloakUserRepresentation>(response, cancellationToken);
    }

    private async Task<IdentityUser> EnrichUserAsync(
        KeycloakUserRepresentation user,
        CancellationToken cancellationToken)
    {
        var userId = user.Id
            ?? throw new IdentityManagementUnavailableException(
                "Identity provider returned a user without an identifier.");
        var rolesTask = GetRolesAsync(
            AdminPath($"users/{Escape(userId)}/role-mappings/realm/composite"),
            cancellationToken);
        var credentialsTask = GetCredentialTypesAsync(userId, cancellationToken);
        await Task.WhenAll(rolesTask, credentialsTask);
        var roles = await rolesTask;
        var credentialTypes = await credentialsTask;
        var requiredActions = user.RequiredActions ?? [];

        return new IdentityUser(
            userId,
            user.Username ?? string.Empty,
            user.FirstName,
            user.LastName,
            user.Email,
            ResolveManagedRole(roles),
            user.Enabled is true,
            user.EmailVerified is true,
            credentialTypes.Contains("otp", StringComparer.OrdinalIgnoreCase),
            requiredActions.Contains(ConfigureTotpAction, StringComparer.Ordinal),
            LastLoginAt: null);
    }

    private async Task<IReadOnlyCollection<string>> GetCredentialTypesAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        using var response = await SendAdminAsync(
            HttpMethod.Get,
            AdminPath($"users/{Escape(userId)}/credentials"),
            null,
            cancellationToken);
        EnsureSuccess(response, "get user credentials");
        var credentials = await DeserializeAsync<List<KeycloakCredentialRepresentation>>(
            response,
            cancellationToken) ?? [];
        return credentials
            .Select(item => item.Type)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<List<KeycloakRoleRepresentation>> GetRolesAsync(
        string path,
        CancellationToken cancellationToken)
    {
        using var response = await SendAdminAsync(HttpMethod.Get, path, null, cancellationToken);
        EnsureSuccess(response, "get user roles");
        return await DeserializeAsync<List<KeycloakRoleRepresentation>>(
            response,
            cancellationToken) ?? [];
    }

    private async Task<KeycloakRoleRepresentation> GetRoleAsync(
        string role,
        CancellationToken cancellationToken)
    {
        using var response = await SendAdminAsync(
            HttpMethod.Get,
            AdminPath($"roles/{Escape(role)}"),
            null,
            cancellationToken);
        EnsureSuccess(response, "get realm role");
        return await DeserializeAsync<KeycloakRoleRepresentation>(response, cancellationToken)
            ?? throw new IdentityManagementUnavailableException(
                "Identity provider returned an invalid role representation.");
    }

    private async Task SendRoleMappingAsync(
        HttpMethod method,
        string userId,
        IReadOnlyCollection<KeycloakRoleRepresentation> roles,
        CancellationToken cancellationToken)
    {
        using var response = await SendAdminAsync(
            method,
            AdminPath($"users/{Escape(userId)}/role-mappings/realm"),
            roles,
            cancellationToken);
        EnsureSuccess(response, "update user role mappings");
    }

    private async Task PutUserRepresentationAsync(
        string userId,
        KeycloakUserRepresentation representation,
        CancellationToken cancellationToken)
    {
        using var response = await SendAdminAsync(
            HttpMethod.Put,
            AdminPath($"users/{Escape(userId)}"),
            representation,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new IdentityUserNotFoundException(userId);
        }

        EnsureSuccess(response, "update user");
    }

    private async Task TryDeleteCreatedUserAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAdminAsync(
                HttpMethod.Delete,
                AdminPath($"users/{Escape(userId)}"),
                null,
                cancellationToken);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            {
                logger.LogWarning(
                    "Compensating identity user delete failed with status {StatusCode}.",
                    (int)response.StatusCode);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Compensating identity user delete could not be completed.");
        }
    }

    private async Task<HttpResponseMessage> SendAdminAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken,
        bool allowTokenRetry = true)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                await GetAccessTokenAsync(cancellationToken));
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: JsonOptions);
            }

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized && allowTokenRetry)
            {
                response.Dispose();
                InvalidateToken();
                return await SendAdminAsync(
                    method,
                    path,
                    body,
                    cancellationToken,
                    allowTokenRetry: false);
            }

            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IdentityManagementUnavailableException(
                "Identity provider request timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new IdentityManagementUnavailableException(
                "Identity provider could not be reached.",
                exception);
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken)
            && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(15))
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(_accessToken)
                && _accessTokenExpiresAt > DateTimeOffset.UtcNow.AddSeconds(15))
            {
                return _accessToken;
            }

            var secret = _options.ClientSecret;
            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new IdentityManagementUnavailableException(
                    "Keycloak admin service account is not configured.");
            }

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = secret
            });
            using var response = await httpClient.PostAsync(
                $"realms/{Escape(_options.Realm)}/protocol/openid-connect/token",
                content,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Keycloak service-account token request failed with status {StatusCode}.",
                    (int)response.StatusCode);
                throw new IdentityManagementUnavailableException(
                    "Keycloak admin service account could not authenticate.");
            }

            var token = await DeserializeAsync<KeycloakTokenResponse>(response, cancellationToken)
                ?? throw new IdentityManagementUnavailableException(
                    "Keycloak returned an invalid service-account token response.");
            if (string.IsNullOrWhiteSpace(token.AccessToken))
            {
                throw new IdentityManagementUnavailableException(
                    "Keycloak returned an empty service-account access token.");
            }

            _accessToken = token.AccessToken;
            _accessTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(
                Math.Max(30, token.ExpiresIn - 30));
            return _accessToken;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IdentityManagementUnavailableException(
                "Identity provider token request timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new IdentityManagementUnavailableException(
                "Identity provider could not be reached.",
                exception);
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void InvalidateToken()
    {
        _accessToken = null;
        _accessTokenExpiresAt = default;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new IdentityManagementConflictException(
                "A user with the same username or email already exists.");
        }

        if ((int)response.StatusCode >= 500
            || response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new IdentityManagementUnavailableException(
                $"Identity provider could not {operation}.");
        }

        throw new InvalidOperationException("Identity provider rejected the user management request.");
    }

    private static async Task<T?> DeserializeAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IdentityManagementUnavailableException(
                "Identity provider response timed out.");
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new IdentityManagementUnavailableException(
                "Identity provider returned an invalid response.",
                exception);
        }
    }

    private string AdminPath(string suffix) =>
        $"admin/realms/{Escape(_options.Realm)}/{suffix}";

    private static string BuildUsersQuery(
        string path,
        int first,
        int max,
        string? search,
        bool? enabled)
    {
        var query = $"{path}?first={first}&max={max}&briefRepresentation=false";
        if (!string.IsNullOrWhiteSpace(search))
        {
            query += $"&search={Uri.EscapeDataString(search)}";
        }

        if (enabled.HasValue)
        {
            query += $"&enabled={enabled.Value.ToString().ToLowerInvariant()}";
        }

        return query;
    }

    private static string BuildUserCountQuery(string? search, bool? enabled)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(search))
        {
            values.Add($"search={Uri.EscapeDataString(search)}");
        }

        if (enabled.HasValue)
        {
            values.Add($"enabled={enabled.Value.ToString().ToLowerInvariant()}");
        }

        return values.Count == 0 ? "users/count" : $"users/count?{string.Join('&', values)}";
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static bool IsManagedRole(string? role) => role is
        "dbscanner-viewer" or "dbscanner-operator" or "dbscanner-admin";

    private static string? ResolveManagedRole(IEnumerable<KeycloakRoleRepresentation> roles)
    {
        var roleNames = roles.Select(role => role.Name).ToHashSet(StringComparer.Ordinal);
        if (roleNames.Contains("dbscanner-admin"))
        {
            return "dbscanner-admin";
        }

        if (roleNames.Contains("dbscanner-operator"))
        {
            return "dbscanner-operator";
        }

        return roleNames.Contains("dbscanner-viewer") ? "dbscanner-viewer" : null;
    }

    private static List<string> AddRequiredAction(List<string>? actions, string action)
    {
        var result = actions?.ToList() ?? [];
        if (!result.Contains(action, StringComparer.Ordinal))
        {
            result.Add(action);
        }

        return result;
    }

    private static List<string> RemoveRequiredAction(List<string>? actions, string action) =>
        (actions ?? [])
            .Where(item => !string.Equals(item, action, StringComparison.Ordinal))
            .ToList();

    private sealed class KeycloakUserRepresentation
    {
        public string? Id { get; set; }
        public string? Username { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public bool? Enabled { get; set; }
        public bool? EmailVerified { get; set; }
        public List<string>? RequiredActions { get; set; }
        public List<KeycloakCredentialRepresentation>? Credentials { get; set; }
        public string? ServiceAccountClientId { get; set; }
    }

    private sealed class KeycloakRoleRepresentation
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public bool? Composite { get; set; }
        public bool? ClientRole { get; set; }
        public string? ContainerId { get; set; }
    }

    private sealed class KeycloakCredentialRepresentation
    {
        public string? Type { get; set; }
        public string? Value { get; set; }
        public bool? Temporary { get; set; }
    }

    private sealed class KeycloakTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
