namespace DbScanner.Application.Models;

/// <summary>
/// Output produced by one executor strategy before it is merged with the other
/// executor outputs for the scan.
/// </summary>
public sealed record ScanExecutorResult(
    string StructuredOutput,
    IReadOnlyList<CheckExecutionResult> CheckResults,
    IReadOnlyList<ScanExecutionArtifact> Artifacts);
