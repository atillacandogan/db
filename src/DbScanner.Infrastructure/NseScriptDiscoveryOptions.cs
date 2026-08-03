namespace DbScanner.Infrastructure;

public sealed class NseScriptDiscoveryOptions
{
    public const string SectionName = "NseScriptDiscovery";

    public string ScriptDirectory { get; set; } = "/usr/share/nmap/scripts";
}
