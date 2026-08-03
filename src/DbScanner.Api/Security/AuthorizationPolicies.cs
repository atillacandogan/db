namespace DbScanner.Api.Security;

internal static class AuthorizationPolicies
{
    public const string Viewer = "Viewer";
    public const string Operator = "Operator";
    public const string Admin = "Admin";
}

internal static class AuthorizationRoles
{
    public const string Viewer = "dbscanner-viewer";
    public const string Operator = "dbscanner-operator";
    public const string Admin = "dbscanner-admin";
}
