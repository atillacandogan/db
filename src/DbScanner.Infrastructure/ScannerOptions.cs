namespace DbScanner.Infrastructure;

public sealed class ScannerOptions
{
    public const string SectionName = "Scanner";

    public string ExecutablePath { get; set; } = "nmap";
    public int TimeoutSeconds { get; set; } = 30;
    public bool UseSampleOutputWhenUnavailable { get; set; } = false;
}
