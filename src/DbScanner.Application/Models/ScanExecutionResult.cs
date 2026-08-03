namespace DbScanner.Application.Models;

public sealed record ScanExecutionResult(
    string RawXml,
    string StructuredOutput,
    IReadOnlyList<ScanScriptExecutionResult> ScriptResults);
