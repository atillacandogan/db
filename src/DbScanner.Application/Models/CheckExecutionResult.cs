using DbScanner.Domain.Enums;

namespace DbScanner.Application.Models;

/// <summary>
/// Normalized result of one check, independent of its executor implementation.
/// </summary>
public sealed record CheckExecutionResult(
    string? CheckId,
    string CheckName,
    string ExecutorType,
    string? RawPayload,
    ScanPayloadFormat PayloadFormat,
    string NormalizedOutput,
    int? ExitCode,
    ProbeResultStatus? ResultStatus = null);
