using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Application.Models;

/// <summary>
/// Compatibility bridge between the current NSE-specific contracts and the
/// executor-independent contracts introduced for the strategy migration.
/// </summary>
public static class LegacyNseScanAdapters
{
    public static ScanCheckDefinition ToScanCheckDefinition(
        this NseScript script,
        string? executorType = null)
    {
        ArgumentNullException.ThrowIfNull(script);
        var resolvedExecutorType = executorType
            ?? script.ExecutorType
            ?? ScanExecutorTypes.NmapNse;
        EnsureValidExecutorType(resolvedExecutorType);

        return new ScanCheckDefinition(
            script.Id,
            script.NseScriptName,
            script.DisplayName,
            script.Description,
            resolvedExecutorType,
            ParseSupportedEngines(script.SupportedEngines),
            script.Categories,
            script.DefaultEnabled,
            script.RequiresCredentials,
            script.IsIntrusive,
            script.AllowedArgsSchema,
            script.ParserType,
            script.MatcherType,
            script.MatcherExpression,
            script.Severity,
            script.Weight,
            script.RemediationTemplate,
            script.DocUrl,
            ToCheckApprovalStatus(script.ApprovalStatus));
    }

    public static NseScript ToNseScript(
        this ScanCheckDefinition check,
        NseScriptSource source = NseScriptSource.Manual,
        bool? isInstalled = null)
    {
        ArgumentNullException.ThrowIfNull(check);

        if (!string.Equals(check.ExecutorType, ScanExecutorTypes.NmapNse, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Executor type '{check.ExecutorType}' cannot be converted to an NSE script.");
        }

        return CreateLegacyNseScript(check, source, isInstalled);
    }

    /// <summary>
    /// Rehydrates the legacy catalog entity used as input by native probes.
    /// Unlike <see cref="ToNseScript"/>, the executor may be native because the
    /// entity is acting only as the transitional check metadata carrier.
    /// </summary>
    public static NseScript ToLegacyNseScript(
        this ScanCheckDefinition check,
        NseScriptSource source = NseScriptSource.Manual,
        bool? isInstalled = null)
    {
        ArgumentNullException.ThrowIfNull(check);
        EnsureValidExecutorType(check.ExecutorType);
        return CreateLegacyNseScript(check, source, isInstalled);
    }

    private static NseScript CreateLegacyNseScript(
        ScanCheckDefinition check,
        NseScriptSource source,
        bool? isInstalled)
    {

        if (check.SupportedEngines.Count == 0)
        {
            throw new InvalidOperationException("An NSE script must support at least one database engine.");
        }

        return new NseScript
        {
            Id = check.Id,
            ExecutorType = check.ExecutorType,
            NseScriptName = check.Name,
            DisplayName = check.DisplayName,
            Description = check.Description,
            SupportedEngines = string.Join(',', check.SupportedEngines.Select(engine => engine.ToApiString())),
            Categories = check.Categories,
            DefaultEnabled = check.DefaultEnabled,
            RequiresCredentials = check.RequiresCredentials,
            IsIntrusive = check.IsIntrusive,
            AllowedArgsSchema = check.ConfigurationSchema,
            ParserType = check.ParserType,
            MatcherType = check.MatcherType,
            MatcherExpression = check.MatcherExpression,
            Severity = check.Severity,
            Weight = check.Weight,
            RemediationTemplate = check.RemediationTemplate,
            DocUrl = check.DocumentationUrl,
            ApprovalStatus = ToScriptApprovalStatus(check.ApprovalStatus),
            Source = source,
            IsInstalled = isInstalled
        };
    }

    public static CheckExecutionResult ToCheckExecutionResult(
        this ScanScriptExecutionResult result,
        string executorType = ScanExecutorTypes.NmapNse)
    {
        ArgumentNullException.ThrowIfNull(result);
        EnsureValidExecutorType(executorType);

        return new CheckExecutionResult(
            result.NseScriptId,
            result.ScriptName,
            executorType,
            result.RawXml,
            ScanPayloadFormat.Xml,
            result.NormalizedOutput,
            result.ExitCode,
            result.ResultStatus);
    }

    public static ScanScriptExecutionResult ToLegacyScanScriptExecutionResult(
        this CheckExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.PayloadFormat != ScanPayloadFormat.Xml)
        {
            throw new InvalidOperationException(
                $"Payload format '{result.PayloadFormat}' cannot be represented by the legacy XML contract.");
        }

        if (result.ExitCode is null)
        {
            throw new InvalidOperationException("A legacy scan script result requires an exit code.");
        }

        return new ScanScriptExecutionResult(
            result.CheckId,
            result.CheckName,
            result.RawPayload,
            result.NormalizedOutput,
            result.ExitCode.Value,
            result.ResultStatus);
    }

    public static CompositeScanExecutionResult ToCompositeScanExecutionResult(
        this ScanExecutionResult result,
        string executorType = ScanExecutorTypes.NmapNse)
    {
        ArgumentNullException.ThrowIfNull(result);
        EnsureValidExecutorType(executorType);

        return new CompositeScanExecutionResult(
            result.StructuredOutput,
            result.ScriptResults.Select(item => item.ToCheckExecutionResult(executorType)).ToArray(),
            new[]
            {
                new ScanExecutionArtifact(executorType, result.RawXml, ScanPayloadFormat.Xml)
            });
    }

    public static ScanExecutionResult ToLegacyScanExecutionResult(
        this CompositeScanExecutionResult result,
        string executorType = ScanExecutorTypes.NmapNse)
    {
        ArgumentNullException.ThrowIfNull(result);
        EnsureValidExecutorType(executorType);

        var incompatibleResult = result.CheckResults.FirstOrDefault(item =>
            !string.Equals(item.ExecutorType, executorType, StringComparison.Ordinal));
        if (incompatibleResult is not null)
        {
            throw new InvalidOperationException(
                $"A composite result containing executor '{incompatibleResult.ExecutorType}' " +
                $"cannot be converted to the '{executorType}' legacy result.");
        }

        var compatibleArtifacts = result.Artifacts
            .Where(item => string.Equals(item.ExecutorType, executorType, StringComparison.Ordinal))
            .ToArray();

        if (compatibleArtifacts.Length != 1 || compatibleArtifacts[0].PayloadFormat != ScanPayloadFormat.Xml)
        {
            throw new InvalidOperationException(
                "The legacy scan result requires exactly one XML artifact for its executor.");
        }

        return new ScanExecutionResult(
            compatibleArtifacts[0].RawPayload ?? string.Empty,
            result.StructuredOutput,
            result.CheckResults.Select(item => item.ToLegacyScanScriptExecutionResult()).ToArray());
    }

    private static IReadOnlyList<DatabaseEngine> ParseSupportedEngines(string supportedEngines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(supportedEngines);

        var engines = new List<DatabaseEngine>();
        foreach (var value in supportedEngines.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!DatabaseEngineExtensions.TryParseApiString(value, out var engine))
            {
                throw new InvalidOperationException($"Unknown database engine '{value}'.");
            }

            if (!engines.Contains(engine))
            {
                engines.Add(engine);
            }
        }

        if (engines.Count == 0)
        {
            throw new InvalidOperationException("An NSE script must support at least one database engine.");
        }

        return engines;
    }

    private static void EnsureValidExecutorType(string executorType)
    {
        if (!ScanExecutorTypes.IsValid(executorType))
        {
            throw new ArgumentException($"Invalid scan executor type '{executorType}'.", nameof(executorType));
        }
    }

    private static CheckApprovalStatus ToCheckApprovalStatus(ScriptApprovalStatus status) => status switch
    {
        ScriptApprovalStatus.Draft => CheckApprovalStatus.Draft,
        ScriptApprovalStatus.Approved => CheckApprovalStatus.Approved,
        ScriptApprovalStatus.Disabled => CheckApprovalStatus.Disabled,
        _ => throw new InvalidOperationException($"Unknown script approval status '{status}'.")
    };

    private static ScriptApprovalStatus ToScriptApprovalStatus(CheckApprovalStatus status) => status switch
    {
        CheckApprovalStatus.Draft => ScriptApprovalStatus.Draft,
        CheckApprovalStatus.Approved => ScriptApprovalStatus.Approved,
        CheckApprovalStatus.Disabled => ScriptApprovalStatus.Disabled,
        _ => throw new InvalidOperationException($"Unknown check approval status '{status}'.")
    };
}
