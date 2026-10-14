namespace DbScanner.Application.Models;

public sealed record ScanExecutionArtifact(
    string ExecutorType,
    string? RawPayload,
    ScanPayloadFormat PayloadFormat);

/// <summary>
/// Combined output from one or more executor strategies.
/// </summary>
public sealed record CompositeScanExecutionResult(
    string StructuredOutput,
    IReadOnlyList<CheckExecutionResult> CheckResults,
    IReadOnlyList<ScanExecutionArtifact> Artifacts);
