namespace DbScanner.Infrastructure.Scanning;

public sealed class CredentialProtectionOptions
{
    public const string SectionName = "CredentialProtection";

    public string? Key { get; set; }
    public string? KeyFile { get; set; }
}
