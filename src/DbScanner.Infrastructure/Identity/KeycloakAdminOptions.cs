namespace DbScanner.Infrastructure.Identity;

public sealed class KeycloakAdminOptions
{
    public const string SectionName = "KeycloakAdmin";

    public string BaseUrl { get; set; } = "http://localhost:18080";
    public string Realm { get; set; } = "dbscanner";
    public string ClientId { get; set; } = "dbscanner-admin-service";
    public string? ClientSecret { get; set; }
    public string WebClientId { get; set; } = "dbscanner-web";
    public string WebRedirectUri { get; set; } = "http://localhost:3000";
    public int EmailActionLifespanSeconds { get; set; } = 1800;
    public int RequestTimeoutSeconds { get; set; } = 15;
}
