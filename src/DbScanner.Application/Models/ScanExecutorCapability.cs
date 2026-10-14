using DbScanner.Domain.Enums;

namespace DbScanner.Application.Models;

public sealed record ScanExecutorCapability(
    string ExecutorType,
    string DisplayName,
    string Description,
    IReadOnlyCollection<DatabaseEngine> SupportedEngines,
    bool AllowsManualCheckCreation,
    bool AllowsCheckNameEditing,
    bool SupportsConfigurationSchema)
{
    public static ScanExecutorCapability ConservativeDefault(string executorType) =>
        new(
            executorType,
            executorType,
            "Registered scan executor.",
            Enum.GetValues<DatabaseEngine>(),
            AllowsManualCheckCreation: false,
            AllowsCheckNameEditing: false,
            SupportsConfigurationSchema: false);
}
