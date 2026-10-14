namespace DbScanner.Contracts.ScanChecks;

public sealed record ScanExecutorCapabilityDto(
    string ExecutorType,
    string DisplayName,
    string Description,
    IReadOnlyList<string> SupportedEngines,
    bool AllowsManualCheckCreation,
    bool AllowsCheckNameEditing,
    bool SupportsConfigurationSchema);
