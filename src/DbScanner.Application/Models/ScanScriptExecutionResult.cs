using DbScanner.Domain.Enums;

namespace DbScanner.Application.Models;

public sealed record ScanScriptExecutionResult(
    string? NseScriptId,
    string ScriptName,
    string? RawXml,
    string NormalizedOutput,
    int ExitCode,
    ProbeResultStatus? ResultStatus = null);
