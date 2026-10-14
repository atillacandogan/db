using DbScanner.Application.Ports;
using DbScanner.Application.Models;
using DbScanner.Application.Observability;
using DbScanner.Contracts.Rules;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Application.Services;

public sealed class RuleCatalogService(IRuleRepository ruleRepository) : IRuleCatalogService
{
    public async Task<IReadOnlyList<RuleDto>> ListRulesAsync(CancellationToken cancellationToken = default)
    {
        var rules = await ruleRepository.ListAsync(cancellationToken);

        return rules
            .Select(MapRule)
            .ToArray();
    }

    public async Task<IReadOnlyList<RuleSupportMatrixDto>> ListSupportMatrixAsync(CancellationToken cancellationToken = default)
    {
        var rules = await ruleRepository.ListAsync(cancellationToken);

        return rules
            .SelectMany(rule => rule.EngineSupport.Select(support => new RuleSupportMatrixDto(
                rule.Id,
                ToEngine(support.Engine),
                ToResultStatus(support.UnsupportedStatus))))
            .OrderBy(item => item.RuleId)
            .ThenBy(item => item.Engine)
            .ToArray();
    }

    public async Task<RuleDto> CreateRuleAsync(CreateRuleCommand command, CancellationToken cancellationToken = default)
    {
        var scanChecks = ScanCheckSelectionCompatibility.Resolve(command.ScanChecks, command.NseScripts);
        Validate(command.Id, command.Title, command.Description, command.MatcherExpression, command.Remediation, command.SupportedEngines, scanChecks, command.Weight);

        var existingRule = await ruleRepository.GetByIdAsync(command.Id, cancellationToken);
        if (existingRule is not null)
        {
            throw new InvalidOperationException("A rule with the same id already exists.");
        }

        var rule = new Rule
        {
            Id = command.Id.Trim(),
            Title = command.Title.Trim(),
            Description = command.Description.Trim(),
            Engine = ParseEngine(command.Engine),
            Severity = ParseSeverity(command.Severity),
            Weight = command.Weight,
            Enabled = command.Enabled,
            ParserType = ParseParserType(command.ParserType),
            MatcherType = ParseMatcherType(command.MatcherType),
            MatcherExpression = command.MatcherExpression.Trim(),
            Remediation = command.Remediation.Trim()
        };

        ApplyRelationships(rule, command.SupportedEngines, scanChecks!);

        await ruleRepository.AddAsync(rule, cancellationToken);
        await ruleRepository.SaveChangesAsync(cancellationToken);

        return MapRule(rule);
    }

    public async Task<RuleDto> UpdateRuleAsync(string id, UpdateRuleCommand command, CancellationToken cancellationToken = default)
    {
        var scanChecks = ScanCheckSelectionCompatibility.Resolve(command.ScanChecks, command.NseScripts);
        Validate(id, command.Title, command.Description, command.MatcherExpression, command.Remediation, command.SupportedEngines, scanChecks, command.Weight);

        var rule = await ruleRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Rule was not found.");

        rule.Title = command.Title.Trim();
        rule.Description = command.Description.Trim();
        rule.Engine = ParseEngine(command.Engine);
        rule.Severity = ParseSeverity(command.Severity);
        rule.Weight = command.Weight;
        rule.Enabled = command.Enabled;
        rule.ParserType = ParseParserType(command.ParserType);
        rule.MatcherType = ParseMatcherType(command.MatcherType);
        rule.MatcherExpression = command.MatcherExpression.Trim();
        rule.Remediation = command.Remediation.Trim();

        ApplyRelationships(rule, command.SupportedEngines, scanChecks!);

        await ruleRepository.SaveChangesAsync(cancellationToken);
        return MapRule(rule);
    }

    private static void ApplyRelationships(
        Rule rule,
        IReadOnlyList<string> supportedEngines,
        IReadOnlyCollection<string> scanChecks)
    {
        var requestedEngines = supportedEngines
            .Select(ParseEngine)
            .Where(engine => engine != DatabaseEngine.Generic)
            .Distinct()
            .ToHashSet();

        foreach (var support in rule.EngineSupport
                     .Where(support => !requestedEngines.Contains(support.Engine))
                     .ToArray())
        {
            rule.EngineSupport.Remove(support);
        }

        var existingEngines = rule.EngineSupport
            .Select(support => support.Engine)
            .ToHashSet();

        foreach (var engine in requestedEngines.Where(engine => !existingEngines.Contains(engine)))
        {
            rule.EngineSupport.Add(new RuleEngineSupport
            {
                RuleId = rule.Id,
                Engine = engine,
                UnsupportedStatus = RuleResultStatus.NotSupported
            });
        }

        var requestedCheckIds = scanChecks
            .Select(check => check.Trim())
            .Where(check => !string.IsNullOrWhiteSpace(check))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in rule.RuleScripts
                     .Where(mapping => !requestedCheckIds.Contains(
                         ScanCheckIdCompatibility.Resolve(
                             mapping.ScanCheckId,
                             mapping.NseScriptId,
                             "rule-mapping")!))
                     .ToArray())
        {
            rule.RuleScripts.Remove(mapping);
        }

        foreach (var mapping in rule.RuleScripts)
        {
            if (mapping.ScanCheckId is null)
            {
                mapping.ScanCheckId = mapping.NseScriptId;
                ScanCompatibilityTelemetry.RecordDualWrite("rule-mapping-repair");
            }
        }

        var existingCheckIds = rule.RuleScripts
            .Select(mapping => ScanCheckIdCompatibility.Resolve(
                mapping.ScanCheckId,
                mapping.NseScriptId,
                "rule-mapping")!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var checkId in requestedCheckIds.Where(checkId => !existingCheckIds.Contains(checkId)))
        {
            rule.RuleScripts.Add(new RuleNseScript
            {
                RuleId = rule.Id,
                NseScriptId = checkId,
                ScanCheckId = checkId
            });
            ScanCompatibilityTelemetry.RecordDualWrite("rule-mapping");
        }
    }

    private static RuleDto MapRule(Rule rule) =>
        new(
            rule.Id,
            rule.Title,
            rule.Description,
            ToSeverity(rule.Severity),
            rule.Weight,
            rule.Enabled,
            ToEngine(rule.Engine),
            ToParserType(rule.ParserType),
            ToMatcherType(rule.MatcherType),
            rule.MatcherExpression,
            rule.Remediation,
            rule.EngineSupport.Select(support => ToEngine(support.Engine)).OrderBy(engine => engine).ToArray(),
            rule.RuleScripts
                .Select(mapping => ScanCheckIdCompatibility.Resolve(
                    mapping.ScanCheckId,
                    mapping.NseScriptId,
                    "rule-response")!)
                .OrderBy(id => id)
                .ToArray());

    private static void Validate(
        string id,
        string title,
        string description,
        string matcherExpression,
        string remediation,
        IReadOnlyList<string> supportedEngines,
        IReadOnlyCollection<string>? scanChecks,
        int weight)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException("Rule id is required.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("Rule title is required.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new InvalidOperationException("Rule description is required.");
        }

        if (string.IsNullOrWhiteSpace(matcherExpression))
        {
            throw new InvalidOperationException("Matcher expression is required.");
        }

        if (string.IsNullOrWhiteSpace(remediation))
        {
            throw new InvalidOperationException("Remediation is required.");
        }

        if (supportedEngines.Count == 0)
        {
            throw new InvalidOperationException("At least one supported engine is required.");
        }

        if (scanChecks is null || scanChecks.Count == 0)
        {
            throw new InvalidOperationException("At least one scan check mapping is required.");
        }

        if (weight < 0)
        {
            throw new InvalidOperationException("Weight cannot be negative.");
        }
    }

    private static string ToSeverity(Severity severity) => severity.ToString().ToLowerInvariant();
    private static string ToParserType(ParserType parserType) => parserType switch
    {
        ParserType.GenericOutput => "generic_output",
        ParserType.StructuredXml => "structured_xml",
        ParserType.Custom => "custom",
        _ => "generic_output"
    };

    private static string ToMatcherType(MatcherType matcherType) => matcherType switch
    {
        MatcherType.Contains => "contains",
        MatcherType.Regex => "regex",
        MatcherType.KeyValue => "key_value",
        MatcherType.XPath => "xpath",
        MatcherType.Custom => "custom",
        _ => "contains"
    };

    private static string ToEngine(DatabaseEngine engine) => engine.ToApiString();

    private static string ToResultStatus(RuleResultStatus status) => status switch
    {
        RuleResultStatus.Passed => "passed",
        RuleResultStatus.Failed => "failed",
        RuleResultStatus.NotApplicable => "not_applicable",
        RuleResultStatus.NotSupported => "not_supported",
        RuleResultStatus.Error => "error",
        _ => "not_supported"
    };

    private static DatabaseEngine ParseEngine(string engine) =>
        DatabaseEngineExtensions.TryParseApiString(engine, out var parsed)
            ? parsed
            : throw new InvalidOperationException("Unsupported database engine.");

    private static Severity ParseSeverity(string value) => value.Trim().ToLowerInvariant() switch
    {
        "critical" => Severity.Critical,
        "high" => Severity.High,
        "medium" => Severity.Medium,
        "low" => Severity.Low,
        "info" => Severity.Info,
        _ => throw new InvalidOperationException("Unsupported severity.")
    };

    private static ParserType ParseParserType(string value) => value.Trim().ToLowerInvariant() switch
    {
        "generic_output" => ParserType.GenericOutput,
        "structured_xml" => ParserType.StructuredXml,
        "custom" => ParserType.Custom,
        _ => throw new InvalidOperationException("Unsupported parser type.")
    };

    private static MatcherType ParseMatcherType(string value) => value.Trim().ToLowerInvariant() switch
    {
        "contains" => MatcherType.Contains,
        "regex" => MatcherType.Regex,
        "key_value" => MatcherType.KeyValue,
        "xpath" => MatcherType.XPath,
        "custom" => MatcherType.Custom,
        _ => throw new InvalidOperationException("Unsupported matcher type.")
    };
}
