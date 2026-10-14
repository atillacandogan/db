using DbScanner.Application.Observability;

namespace DbScanner.Application.Models;

public static class ScanCheckSelectionCompatibility
{
    public static IReadOnlyCollection<string>? Resolve(
        IReadOnlyCollection<string>? selectedCheckIds,
        IReadOnlyCollection<string>? selectedNseScriptIds)
    {
        if (selectedCheckIds is null)
        {
            if (selectedNseScriptIds is null)
            {
                return null;
            }

            ScanCompatibilityTelemetry.RecordFallback("contract-selection");
            return Normalize(selectedNseScriptIds);
        }

        if (selectedNseScriptIds is null)
        {
            return Normalize(selectedCheckIds);
        }

        var genericIds = Normalize(selectedCheckIds);
        var legacyIds = Normalize(selectedNseScriptIds);
        if (!genericIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
            .SetEquals(legacyIds))
        {
            ScanCompatibilityTelemetry.RecordConflict("contract-selection");
            throw new InvalidOperationException(
                "selectedCheckIds and selectedNseScriptIds must identify the same checks when both are provided.");
        }

        return genericIds;
    }

    private static string[] Normalize(IEnumerable<string> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
