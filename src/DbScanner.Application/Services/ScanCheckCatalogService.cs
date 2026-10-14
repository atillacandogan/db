using System.Text.Json;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Contracts.ScanChecks;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Application.Services;

/// <summary>
/// Executor-neutral facade over the legacy catalog table. During the additive
/// migration, rows without ExecutorType remain readable through the legacy
/// routing rules while new rows persist their strategy identifier explicitly.
/// </summary>
public sealed class ScanCheckCatalogService(
    INseScriptRepository repository,
    IScanExecutorStrategyResolver strategyResolver) : IScanCheckCatalogService
{
    public async Task<IReadOnlyList<ScanCheckDto>> ListAsync(CancellationToken cancellationToken = default) =>
        (await repository.ListAsync(cancellationToken)).Select(Map).ToArray();

    public async Task<ScanCheckDto> CreateAsync(
        CreateScanCheckCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(
            command.Id,
            command.Name,
            command.DisplayName,
            command.Description,
            command.ExecutorType,
            command.SupportedEngines,
            command.Weight);
        var capability = GetCapability(command.ExecutorType);
        ValidateCapability(
            capability,
            command.Name,
            command.SupportedEngines,
            command.ConfigurationSchema,
            creating: true);

        if (await repository.GetByIdAsync(command.Id.Trim(), cancellationToken) is not null)
        {
            throw new InvalidOperationException("A scan check with the same id already exists.");
        }

        var status = ParseApprovalStatus(command.ApprovalStatus);
        var script = new NseScript
        {
            Id = command.Id.Trim(),
            ExecutorType = command.ExecutorType.Trim(),
            NseScriptName = command.Name.Trim(),
            DisplayName = command.DisplayName.Trim(),
            Description = command.Description.Trim(),
            SupportedEngines = JoinEngines(command.SupportedEngines),
            Categories = TrimNullable(command.Categories),
            DefaultEnabled = command.DefaultEnabled && status != ScriptApprovalStatus.Disabled,
            RequiresCredentials = command.RequiresCredentials,
            IsIntrusive = command.IsIntrusive,
            AllowedArgsSchema = NormalizeConfigurationSchema(command.ConfigurationSchema),
            ParserType = ParseParserType(command.ParserType),
            MatcherType = ParseMatcherType(command.MatcherType),
            MatcherExpression = command.MatcherExpression.Trim(),
            Severity = ParseSeverity(command.Severity),
            Weight = command.Weight,
            RemediationTemplate = TrimNullable(command.RemediationTemplate),
            DocUrl = TrimNullable(command.DocumentationUrl),
            ApprovalStatus = status,
            Source = NseScriptSource.Manual
        };

        script.Matchers.Add(CreateMatcher(script, command.SupportedEngines));
        await repository.AddAsync(script, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Map(script);
    }

    public async Task<ScanCheckDto> UpdateAsync(
        string id,
        UpdateScanCheckCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(
            id,
            command.Name,
            command.DisplayName,
            command.Description,
            command.ExecutorType,
            command.SupportedEngines,
            command.Weight);
        var script = await repository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Scan check was not found.");
        var currentExecutorType = LegacyScanCheckExecutorRouting.Resolve(script);
        if (!string.Equals(currentExecutorType, command.ExecutorType.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A scan check executor cannot be changed. Create a new check instead.");
        }

        var capability = GetCapability(command.ExecutorType);
        ValidateCapability(
            capability,
            command.Name,
            command.SupportedEngines,
            command.ConfigurationSchema,
            creating: false,
            existingName: script.NseScriptName,
            existingSupportedEngines: SplitEngines(script.SupportedEngines),
            existingConfigurationSchema: script.AllowedArgsSchema);
        var status = ParseApprovalStatus(command.ApprovalStatus);

        script.ExecutorType = command.ExecutorType.Trim();
        script.NseScriptName = command.Name.Trim();
        script.DisplayName = command.DisplayName.Trim();
        script.Description = command.Description.Trim();
        script.SupportedEngines = JoinEngines(command.SupportedEngines);
        script.Categories = TrimNullable(command.Categories);
        script.DefaultEnabled = command.DefaultEnabled && status != ScriptApprovalStatus.Disabled;
        script.RequiresCredentials = command.RequiresCredentials;
        script.IsIntrusive = command.IsIntrusive;
        script.AllowedArgsSchema = NormalizeConfigurationSchema(command.ConfigurationSchema);
        script.ParserType = ParseParserType(command.ParserType);
        script.MatcherType = ParseMatcherType(command.MatcherType);
        script.MatcherExpression = command.MatcherExpression.Trim();
        script.Severity = ParseSeverity(command.Severity);
        script.Weight = command.Weight;
        script.RemediationTemplate = TrimNullable(command.RemediationTemplate);
        script.DocUrl = TrimNullable(command.DocumentationUrl);
        script.ApprovalStatus = status;

        var matcher = script.Matchers.FirstOrDefault();
        if (matcher is null)
        {
            script.Matchers.Add(CreateMatcher(script, command.SupportedEngines));
        }
        else
        {
            matcher.MatcherExpression = script.MatcherExpression ?? string.Empty;
            matcher.MatcherType = script.MatcherType;
            matcher.ParserType = script.ParserType;
            matcher.Engine = ParsePrimaryEngine(command.SupportedEngines);
        }

        await repository.SaveChangesAsync(cancellationToken);
        return Map(script);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var script = await repository.GetByIdAsync(id, cancellationToken);
        if (script is null)
        {
            return false;
        }

        if (script.Source == NseScriptSource.Installed)
        {
            throw new InvalidOperationException(
                "Installed scan checks cannot be deleted. Disable the check instead.");
        }

        repository.Delete(script);
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    private ScanExecutorCapability GetCapability(string executorType) =>
        strategyResolver.Resolve(executorType.Trim()).Capability;

    private static void ValidateCapability(
        ScanExecutorCapability capability,
        string name,
        IReadOnlyList<string> supportedEngines,
        string? configurationSchema,
        bool creating,
        string? existingName = null,
        IReadOnlyCollection<string>? existingSupportedEngines = null,
        string? existingConfigurationSchema = null)
    {
        if (creating && !capability.AllowsManualCheckCreation)
        {
            throw new InvalidOperationException(
                $"Executor '{capability.ExecutorType}' does not allow manual scan-check creation.");
        }

        if (!creating
            && !capability.AllowsCheckNameEditing
            && !string.Equals(existingName, name.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Executor '{capability.ExecutorType}' does not allow changing the check name.");
        }

        var requestedEngines = ParseEngines(supportedEngines);
        var unsupportedEngines = requestedEngines
            .Where(engine => !capability.SupportedEngines.Contains(engine))
            .Select(engine => engine.ToApiString())
            .ToArray();
        var enginesChanged = existingSupportedEngines is null
            || !requestedEngines.Select(engine => engine.ToApiString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(existingSupportedEngines);
        if (unsupportedEngines.Length > 0 && (creating || enginesChanged))
        {
            throw new InvalidOperationException(
                $"Executor '{capability.ExecutorType}' does not support engines: " +
                string.Join(", ", unsupportedEngines));
        }

        if (!capability.SupportsConfigurationSchema
            && !string.IsNullOrWhiteSpace(configurationSchema)
            && (creating || !string.Equals(
                NormalizeConfigurationSchema(configurationSchema),
                NormalizeConfigurationSchema(existingConfigurationSchema),
                StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Executor '{capability.ExecutorType}' does not support a configuration schema.");
        }
    }

    private static void Validate(
        string id,
        string name,
        string displayName,
        string description,
        string executorType,
        IReadOnlyList<string> supportedEngines,
        int weight)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("Scan check id is required.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Scan check name is required.");
        }

        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(description))
        {
            throw new InvalidOperationException("Display name and description are required.");
        }

        if (!ScanExecutorTypes.IsValid(executorType?.Trim()))
        {
            throw new InvalidOperationException("Executor type is invalid.");
        }

        if (supportedEngines is null || supportedEngines.Count == 0)
        {
            throw new InvalidOperationException("At least one supported engine is required.");
        }

        _ = ParseEngines(supportedEngines);
        if (weight < 0)
        {
            throw new InvalidOperationException("Weight cannot be negative.");
        }
    }

    private static NseScriptMatcher CreateMatcher(NseScript script, IReadOnlyList<string> supportedEngines) =>
        new()
        {
            NseScriptId = script.Id,
            MatcherExpression = script.MatcherExpression ?? string.Empty,
            MatcherType = script.MatcherType,
            ParserType = script.ParserType,
            Engine = ParsePrimaryEngine(supportedEngines)
        };

    private static ScanCheckDto Map(NseScript script) =>
        new(
            script.Id,
            script.NseScriptName,
            script.DisplayName,
            script.Description,
            LegacyScanCheckExecutorRouting.Resolve(script),
            SplitEngines(script.SupportedEngines),
            script.Categories,
            script.DefaultEnabled,
            script.RequiresCredentials,
            script.IsIntrusive,
            script.AllowedArgsSchema,
            FormatParserType(script.ParserType),
            FormatMatcherType(script.MatcherType),
            script.MatcherExpression ?? string.Empty,
            script.Severity.ToString().ToLowerInvariant(),
            script.Weight,
            script.RemediationTemplate,
            script.DocUrl,
            script.ApprovalStatus.ToString().ToLowerInvariant(),
            script.Source.ToString().ToLowerInvariant(),
            script.IsInstalled,
            script.Source == NseScriptSource.Manual);

    private static IReadOnlyList<DatabaseEngine> ParseEngines(IEnumerable<string> values)
    {
        var engines = new List<DatabaseEngine>();
        foreach (var value in values)
        {
            if (!DatabaseEngineExtensions.TryParseApiString(value?.Trim(), out var engine))
            {
                throw new InvalidOperationException($"Unsupported database engine '{value}'.");
            }

            if (!engines.Contains(engine))
            {
                engines.Add(engine);
            }
        }

        return engines;
    }

    private static string JoinEngines(IReadOnlyList<string> values) =>
        string.Join(',', ParseEngines(values).Select(engine => engine.ToApiString()));

    private static string[] SplitEngines(string values) =>
        values.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => value.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static DatabaseEngine ParsePrimaryEngine(IReadOnlyList<string> values) =>
        ParseEngines(values).FirstOrDefault();

    private static ScriptApprovalStatus ParseApprovalStatus(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "draft" => ScriptApprovalStatus.Draft,
            "approved" => ScriptApprovalStatus.Approved,
            "disabled" => ScriptApprovalStatus.Disabled,
            _ => throw new InvalidOperationException("Unsupported check approval status.")
        };

    private static ParserType ParseParserType(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "generic_output" or "genericoutput" => ParserType.GenericOutput,
            "structured_xml" or "structuredxml" => ParserType.StructuredXml,
            "custom" => ParserType.Custom,
            _ => throw new InvalidOperationException("Unsupported parser type.")
        };

    private static MatcherType ParseMatcherType(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "contains" => MatcherType.Contains,
            "regex" => MatcherType.Regex,
            "key_value" or "keyvalue" => MatcherType.KeyValue,
            "xpath" => MatcherType.XPath,
            "custom" => MatcherType.Custom,
            _ => throw new InvalidOperationException("Unsupported matcher type.")
        };

    private static Severity ParseSeverity(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "critical" => Severity.Critical,
            "high" => Severity.High,
            "medium" => Severity.Medium,
            "low" => Severity.Low,
            "info" => Severity.Info,
            _ => throw new InvalidOperationException("Unsupported severity.")
        };

    private static string FormatParserType(ParserType value) => value switch
    {
        ParserType.GenericOutput => "generic_output",
        ParserType.StructuredXml => "structured_xml",
        ParserType.Custom => "custom",
        _ => throw new InvalidOperationException("Unsupported parser type.")
    };

    private static string FormatMatcherType(MatcherType value) => value switch
    {
        MatcherType.Contains => "contains",
        MatcherType.Regex => "regex",
        MatcherType.KeyValue => "key_value",
        MatcherType.XPath => "xpath",
        MatcherType.Custom => "custom",
        _ => throw new InvalidOperationException("Unsupported matcher type.")
    };

    private static string? TrimNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeConfigurationSchema(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.GetRawText();
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Configuration schema must be valid JSON.");
        }
    }
}
