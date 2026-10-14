using System.Diagnostics.Metrics;

namespace DbScanner.Application.Observability;

public static class ScanCompatibilityTelemetry
{
    public const string MeterName = "DbScanner.ScanCompatibility";

    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> FallbackCounter = Meter.CreateCounter<long>(
        "dbscanner.scan_check.compatibility_fallbacks",
        unit: "{fallback}",
        description: "Number of times a legacy scan-check compatibility fallback was used.");
    private static readonly Counter<long> DualWriteCounter = Meter.CreateCounter<long>(
        "dbscanner.scan_check.compatibility_dual_writes",
        unit: "{write}",
        description: "Number of writes that populated generic and legacy scan-check identifiers.");
    private static readonly Counter<long> ConflictCounter = Meter.CreateCounter<long>(
        "dbscanner.scan_check.compatibility_conflicts",
        unit: "{conflict}",
        description: "Number of rejected requests containing conflicting generic and legacy identifiers.");
    private static int readinessState = -1;
    private static readonly ObservableGauge<int> ReadinessGauge = Meter.CreateObservableGauge(
        "dbscanner.scan_check.compatibility_ready",
        () => Volatile.Read(ref readinessState),
        unit: "{state}",
        description: "Legacy-removal readiness: 1 ready, 0 blocked, -1 not audited.");

    public static void RecordFallback(string surface) =>
        FallbackCounter.Add(1, new KeyValuePair<string, object?>("surface", surface));

    public static void RecordDualWrite(string surface) =>
        DualWriteCounter.Add(1, new KeyValuePair<string, object?>("surface", surface));

    public static void RecordConflict(string surface) =>
        ConflictCounter.Add(1, new KeyValuePair<string, object?>("surface", surface));

    public static void RecordReadiness(bool ready) =>
        Volatile.Write(ref readinessState, ready ? 1 : 0);
}
