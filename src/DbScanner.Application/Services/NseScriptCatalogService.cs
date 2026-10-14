using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Contracts.NseScripts;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Application.Services;

public sealed class NseScriptCatalogService(INseScriptRepository nseScriptRepository) : INseScriptCatalogService
{
    public async Task<IReadOnlyList<NseScriptDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var scripts = await nseScriptRepository.ListAsync(cancellationToken);
        return scripts.Select(MapScript).ToArray();
    }

    public async Task<NseScriptDto> CreateAsync(CreateNseScriptCommand command, CancellationToken cancellationToken = default)
    {
        Validate(command.Id, command.NseScriptName, command.DisplayName, command.Description, command.SupportedEngines, command.Weight);
        var approvalStatus = ParseApprovalStatus(command.ApprovalStatus);

        var existingScript = await nseScriptRepository.GetByIdAsync(command.Id, cancellationToken);
        if (existingScript is not null)
        {
            throw new InvalidOperationException("A script with the same id already exists.");
        }

        var script = new NseScript
        {
            Id = command.Id.Trim(),
            NseScriptName = command.NseScriptName.Trim(),
            DisplayName = command.DisplayName.Trim(),
            Description = command.Description.Trim(),
            SupportedEngines = JoinEngines(command.SupportedEngines),
            DefaultEnabled = command.DefaultEnabled && approvalStatus != ScriptApprovalStatus.Disabled,
            ApprovalStatus = approvalStatus,
            MatcherType = ParseMatcherType(command.MatcherType),
            ParserType = ParseParserType(command.ParserType),
            MatcherExpression = command.MatcherExpression.Trim(),
            Severity = ParseSeverity(command.Severity),
            Weight = command.Weight,
            Categories = TrimNullable(command.Categories),
            RemediationTemplate = TrimNullable(command.RemediationTemplate),
            DocUrl = TrimNullable(command.DocUrl),
            Source = NseScriptSource.Manual
        };

        script.ExecutorType = LegacyScanCheckExecutorRouting.Resolve(script);

        script.Matchers.Add(new NseScriptMatcher
        {
            NseScriptId = script.Id,
            MatcherExpression = script.MatcherExpression ?? string.Empty,
            MatcherType = script.MatcherType,
            ParserType = script.ParserType,
            Engine = ParsePrimaryEngine(command.SupportedEngines)
        });

        await nseScriptRepository.AddAsync(script, cancellationToken);
        await nseScriptRepository.SaveChangesAsync(cancellationToken);

        return MapScript(script);
    }

    public async Task<NseScriptDto> UpdateAsync(string id, UpdateNseScriptCommand command, CancellationToken cancellationToken = default)
    {
        Validate(id, id, command.DisplayName, command.Description, command.SupportedEngines, command.Weight);
        var approvalStatus = ParseApprovalStatus(command.ApprovalStatus);

        var script = await nseScriptRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Script was not found.");

        script.DisplayName = command.DisplayName.Trim();
        script.Description = command.Description.Trim();
        script.SupportedEngines = JoinEngines(command.SupportedEngines);
        script.DefaultEnabled = command.DefaultEnabled && approvalStatus != ScriptApprovalStatus.Disabled;
        script.ApprovalStatus = approvalStatus;
        script.MatcherType = ParseMatcherType(command.MatcherType);
        script.ParserType = ParseParserType(command.ParserType);
        script.MatcherExpression = command.MatcherExpression.Trim();
        script.Severity = ParseSeverity(command.Severity);
        script.Weight = command.Weight;
        script.Categories = TrimNullable(command.Categories);
        script.RemediationTemplate = TrimNullable(command.RemediationTemplate);
        script.DocUrl = TrimNullable(command.DocUrl);

        var primaryMatcher = script.Matchers.FirstOrDefault();
        if (primaryMatcher is null)
        {
            script.Matchers.Add(new NseScriptMatcher
            {
                NseScriptId = script.Id,
                MatcherExpression = script.MatcherExpression ?? string.Empty,
                MatcherType = script.MatcherType,
                ParserType = script.ParserType,
                Engine = ParsePrimaryEngine(command.SupportedEngines)
            });
        }
        else
        {
            primaryMatcher.MatcherExpression = script.MatcherExpression ?? string.Empty;
            primaryMatcher.MatcherType = script.MatcherType;
            primaryMatcher.ParserType = script.ParserType;
            primaryMatcher.Engine = ParsePrimaryEngine(command.SupportedEngines);
        }

        await nseScriptRepository.SaveChangesAsync(cancellationToken);
        return MapScript(script);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var script = await nseScriptRepository.GetByIdAsync(id, cancellationToken);
        if (script is null)
        {
            return false;
        }

        if (script.Source == NseScriptSource.Installed)
        {
            throw new InvalidOperationException("Installed NSE scripts cannot be deleted. Disable the script instead.");
        }

        nseScriptRepository.Delete(script);
        await nseScriptRepository.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static void Validate(
        string id,
        string scriptName,
        string displayName,
        string description,
        IReadOnlyList<string> supportedEngines,
        int weight)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("Script id is required.");
        }

        if (string.IsNullOrWhiteSpace(scriptName))
        {
            throw new InvalidOperationException("NSE script name is required.");
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new InvalidOperationException("Display name is required.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new InvalidOperationException("Description is required.");
        }

        if (supportedEngines.Count == 0)
        {
            throw new InvalidOperationException("At least one supported engine is required.");
        }

        if (weight < 0)
        {
            throw new InvalidOperationException("Weight cannot be negative.");
        }
    }

    private static NseScriptDto MapScript(NseScript script) =>
        new(
            script.Id,
            script.NseScriptName,
            script.DisplayName,
            SplitEngines(script.SupportedEngines),
            script.DefaultEnabled,
            script.RequiresCredentials,
            script.IsIntrusive,
            script.ApprovalStatus.ToString().ToLowerInvariant(),
            script.Description,
            FormatMatcherType(script.MatcherType),
            FormatParserType(script.ParserType),
            script.MatcherExpression ?? string.Empty,
            script.Severity.ToString().ToLowerInvariant(),
            script.Weight,
            script.Categories,
            script.RemediationTemplate,
            script.DocUrl,
            script.Source.ToString().ToLowerInvariant(),
            script.IsInstalled,
            script.Source == NseScriptSource.Manual);

    private static string[] SplitEngines(string supportedEngines) =>
        supportedEngines
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(engine => engine.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string JoinEngines(IReadOnlyList<string> supportedEngines) =>
        string.Join(",", supportedEngines
            .Select(engine => engine.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase));

    private static string? TrimNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ScriptApprovalStatus ParseApprovalStatus(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "draft" => ScriptApprovalStatus.Draft,
            "approved" => ScriptApprovalStatus.Approved,
            "disabled" => ScriptApprovalStatus.Disabled,
            _ => throw new InvalidOperationException("Unsupported script approval status.")
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

    private static ParserType ParseParserType(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "generic_output" or "genericoutput" => ParserType.GenericOutput,
            "structured_xml" or "structuredxml" => ParserType.StructuredXml,
            "custom" => ParserType.Custom,
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

    private static string FormatParserType(ParserType value) => value switch
    {
        ParserType.GenericOutput => "generic_output",
        ParserType.StructuredXml => "structured_xml",
        ParserType.Custom => "custom",
        _ => throw new InvalidOperationException("Unsupported parser type.")
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

    private static DatabaseEngine ParsePrimaryEngine(IReadOnlyList<string> supportedEngines)
    {
        var engines = supportedEngines.Select(engine => engine.Trim().ToLowerInvariant()).ToArray();
        if (engines.Contains("postgresql", StringComparer.OrdinalIgnoreCase))
        {
            return DatabaseEngine.PostgreSql;
        }

        if (engines.Contains("mysql", StringComparer.OrdinalIgnoreCase))
        {
            return DatabaseEngine.MySql;
        }

        if (engines.Contains("mssql", StringComparer.OrdinalIgnoreCase))
        {
            return DatabaseEngine.MsSql;
        }

        if (engines.Contains("oracle", StringComparer.OrdinalIgnoreCase))
        {
            return DatabaseEngine.Oracle;
        }

        return DatabaseEngine.Generic;
    }
}
