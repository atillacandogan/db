using DbScanner.Application.Observability;

namespace DbScanner.Application.Models;

public static class ScanCheckIdCompatibility
{
    public static string? Resolve(string? scanCheckId, string? nseScriptId, string surface)
    {
        if (!string.IsNullOrWhiteSpace(scanCheckId))
        {
            if (!string.IsNullOrWhiteSpace(nseScriptId)
                && !string.Equals(scanCheckId, nseScriptId, StringComparison.OrdinalIgnoreCase))
            {
                ScanCompatibilityTelemetry.RecordConflict(surface);
                throw new InvalidOperationException(
                    "ScanCheckId and NseScriptId must identify the same check during compatibility migration.");
            }

            return scanCheckId;
        }

        if (string.IsNullOrWhiteSpace(nseScriptId))
        {
            return null;
        }

        ScanCompatibilityTelemetry.RecordFallback(surface);
        return nseScriptId;
    }
}
