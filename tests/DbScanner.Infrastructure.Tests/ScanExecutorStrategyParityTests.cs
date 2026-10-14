using System.Xml.Linq;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Application.Services;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Scanning;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

public sealed class ScanExecutorStrategyParityTests
{
    [Theory]
    [InlineData("banner", "postgresql", ScanExecutorTypes.NmapNse)]
    [InlineData("MYSQL-EMPTY-PASSWORD", "mysql", ScanExecutorTypes.MySqlNative)]
    [InlineData("mssql-browser-discovery", "mssql", ScanExecutorTypes.MsSqlNative)]
    [InlineData("mssql-auth-version", "mssql", ScanExecutorTypes.MsSqlNative)]
    [InlineData("ms-sql-info", "mssql", ScanExecutorTypes.NmapNse)]
    [InlineData("oracle-auth-version", "oracle", ScanExecutorTypes.OracleNative)]
    [InlineData("oracle-tns-version", "oracle", ScanExecutorTypes.NmapNse)]
    public void Router_assigns_legacy_scripts_to_server_controlled_executors(
        string scriptName,
        string supportedEngine,
        string expectedExecutorType)
    {
        var router = new NseScriptScanCheckRouter();
        var script = CreateScript(scriptName, supportedEngine);

        var check = router.Route(script);

        Assert.Equal(expectedExecutorType, check.ExecutorType);
        Assert.Equal(script.Id, check.Id);
        Assert.Equal(script.NseScriptName, check.Name);
        Assert.Equal(script.RequiresCredentials, check.RequiresCredentials);
        Assert.Equal(CheckApprovalStatus.Approved, check.ApprovalStatus);
    }

    [Theory]
    [InlineData("banner", "postgresql", ScanExecutorTypes.NmapNse)]
    [InlineData("mssql-auth-version", "mssql", ScanExecutorTypes.MsSqlNative)]
    [InlineData("oracle-auth-version", "oracle", ScanExecutorTypes.OracleNative)]
    public void ExplicitAndLegacyCatalogRowsProduceEquivalentExecutionPlans(
        string scriptName,
        string supportedEngine,
        string expectedExecutorType)
    {
        var router = new NseScriptScanCheckRouter();
        var legacy = CreateScript(scriptName, supportedEngine);
        var generic = CreateScript(scriptName, supportedEngine);
        generic.ExecutorType = expectedExecutorType;

        var legacyPlan = router.Route([legacy]);
        var genericPlan = router.Route([generic]);

        var legacyCheck = Assert.Single(legacyPlan);
        var genericCheck = Assert.Single(genericPlan);
        Assert.Equal(legacyCheck.SupportedEngines, genericCheck.SupportedEngines);
        Assert.Equal(
            legacyCheck,
            genericCheck with { SupportedEngines = legacyCheck.SupportedEngines });
        Assert.Equal(expectedExecutorType, genericCheck.ExecutorType);
    }

    [Fact]
    public void ExplicitExecutorMetadataOverridesLegacyNameInference()
    {
        var router = new NseScriptScanCheckRouter();
        var script = CreateScript("mssql-auth-version", "mssql");
        script.ExecutorType = ScanExecutorTypes.ExternalApi;

        var check = router.Route(script);

        Assert.Equal(ScanExecutorTypes.ExternalApi, check.ExecutorType);
    }

    [Fact]
    public async Task PostgreSql_nmap_strategy_matches_the_legacy_runtime()
    {
        var fixture = CreateFixture();
        var target = CreateTarget(DatabaseEngine.PostgreSql);
        var scripts = new[] { CreateScript("banner", "postgresql") };

        await AssertParityAsync(fixture, target, scripts);
    }

    [Fact]
    public async Task MySql_native_compatibility_strategy_matches_the_legacy_runtime()
    {
        var fixture = CreateFixture();
        var target = CreateTarget(DatabaseEngine.MySql);
        var scripts = new[] { CreateScript("mysql-empty-password", "mysql") };

        await AssertParityAsync(fixture, target, scripts);
    }

    [Fact]
    public async Task MySql_compatibility_plan_keeps_preflight_and_native_check_in_one_executor_group()
    {
        var fixture = CreateFixture();
        var target = CreateTarget(DatabaseEngine.MySql);
        var scripts = new[]
        {
            CreateScript("mysql-info", "mysql"),
            CreateScript("mysql-empty-password", "mysql")
        };

        var routed = fixture.Router.Route(scripts);
        var composite = await AssertParityAsync(fixture, target, scripts);

        Assert.All(routed, check =>
            Assert.Equal(ScanExecutorTypes.MySqlNative, check.ExecutorType));
        Assert.Single(composite.Artifacts);
        Assert.Equal(ScanExecutorTypes.MySqlNative, composite.Artifacts[0].ExecutorType);
    }

    [Fact]
    public async Task MsSql_nmap_and_native_strategies_match_the_legacy_runtime()
    {
        var fixture = CreateFixture();
        var target = CreateTarget(DatabaseEngine.MsSql);
        var scripts = new[]
        {
            CreateScript("ms-sql-info", "mssql"),
            CreateScript(MsSqlProbeScripts.BrowserDiscovery, "mssql"),
            CreateScript(MsSqlProbeScripts.Version, "mssql", requiresCredentials: true)
        };
        var authentication = new DatabaseAuthentication(
            "DbScannerDemo",
            "scanner",
            "must-not-appear-in-output");

        var composite = await AssertParityAsync(fixture, target, scripts, authentication);

        Assert.DoesNotContain(
            authentication.Password,
            composite.StructuredOutput,
            StringComparison.Ordinal);
        Assert.All(composite.CheckResults, result => Assert.DoesNotContain(
            authentication.Password,
            result.NormalizedOutput,
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task Oracle_nmap_and_native_strategies_match_the_legacy_runtime()
    {
        var fixture = CreateFixture();
        var target = CreateTarget(DatabaseEngine.Oracle);
        var scripts = new[]
        {
            CreateScript(OracleProbeScripts.TnsVersion, "oracle"),
            CreateScript(OracleProbeScripts.Version, "oracle", requiresCredentials: true)
        };
        var authentication = new DatabaseAuthentication(
            "FREEPDB1",
            "scanner",
            "must-not-appear-in-output");

        var composite = await AssertParityAsync(fixture, target, scripts, authentication);

        Assert.DoesNotContain(
            authentication.Password,
            composite.StructuredOutput,
            StringComparison.Ordinal);
        Assert.All(composite.CheckResults, result => Assert.DoesNotContain(
            authentication.Password,
            result.NormalizedOutput,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Strategy_registry_contains_each_executor_once()
    {
        var fixture = CreateFixture();

        Assert.Equal(
            [
                ScanExecutorTypes.ExternalApi
            ],
            new ScanExecutorStrategyResolver(
                [new StaticStrategy(ScanExecutorTypes.ExternalApi)])
                .RegisteredExecutorTypes);
        Assert.Equal(
            [
                ScanExecutorTypes.MsSqlNative,
                ScanExecutorTypes.MySqlNative,
                ScanExecutorTypes.NmapNse,
                ScanExecutorTypes.OracleNative
            ],
            fixture.Resolver.RegisteredExecutorTypes);
    }

    [Theory]
    [InlineData(DatabaseEngine.MsSql, MsSqlProbeScripts.Version, "mssql")]
    [InlineData(DatabaseEngine.Oracle, OracleProbeScripts.Version, "oracle")]
    public async Task Missing_credentials_preserve_legacy_result_and_rule_behavior(
        DatabaseEngine engine, string scriptName, string supportedEngine)
    {
        await AssertParityAsync(CreateFixture(), CreateTarget(engine),
            [CreateScript(scriptName, supportedEngine, requiresCredentials: true)]);
    }

    [Fact]
    public async Task Native_strategy_rejects_a_target_from_another_database_engine()
    {
        var fixture = CreateFixture();
        var target = CreateTarget(DatabaseEngine.PostgreSql);
        var context = new ScanExecutionContext(
            new Scan { TargetId = target.Id, Target = target },
            target);
        var check = fixture.Router.Route(
            CreateScript(MsSqlProbeScripts.Version, "mssql", requiresCredentials: true));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Resolver.Resolve(ScanExecutorTypes.MsSqlNative).ExecuteAsync(
                context,
                [check]));

        Assert.Contains("postgresql", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<CompositeScanExecutionResult> AssertParityAsync(
        StrategyFixture fixture,
        Target target,
        IReadOnlyCollection<NseScript> scripts,
        DatabaseAuthentication? authentication = null)
    {
        var legacyScan = new Scan { TargetId = target.Id, Target = target };
        var legacyResult = await fixture.LegacyRuntime.RunAsync(
            legacyScan,
            target,
            scripts,
            authentication);

        var strategyScan = new Scan { TargetId = target.Id, Target = target };
        var context = new ScanExecutionContext(
            strategyScan,
            target,
            authentication,
            target.Engine == DatabaseEngine.MySql ? [] : [ScanExecutorTypes.NmapNse]);
        var composite = await fixture.Orchestrator.ExecuteAsync(
            context,
            fixture.Router.Route(scripts));

        Assert.Equal(legacyResult.StructuredOutput, composite.StructuredOutput);
        Assert.Equal(
            legacyResult.ScriptResults.Select(ToComparableResult),
            composite.CheckResults.Select(ToComparableResult));
        Assert.NotEmpty(composite.Artifacts);
        Assert.All(composite.Artifacts, artifact =>
        {
            Assert.Equal(ScanPayloadFormat.Xml, artifact.PayloadFormat);
            Assert.False(string.IsNullOrWhiteSpace(artifact.RawPayload));
            _ = XDocument.Parse(artifact.RawPayload!);
        });

        // Compare the actual production adapter, not just the orchestrator.
        var production = new StrategyScannerRuntime(fixture.Router, fixture.Orchestrator);
        var productionResult = await production.RunAsync(
            strategyScan, target, scripts, authentication);
        Assert.Equal(legacyResult.StructuredOutput, productionResult.StructuredOutput);
        Assert.Equal(
            legacyResult.ScriptResults.Select(ToComparableResult),
            productionResult.ScriptResults.Select(ToComparableResult));

        var scoring = new ScoringService();
        var ruleEngine = new RuleEngine(scoring);
        var rules = scripts.Select(script =>
        {
            var rule = new Rule
            {
                Id = $"parity-{script.Id}",
                Title = script.DisplayName,
                Description = "Old/new execution parity",
                Enabled = true,
                Severity = Severity.High,
                Weight = 4,
                ParserType = ParserType.GenericOutput,
                MatcherType = MatcherType.Regex,
                MatcherExpression = ".+",
                Remediation = "Parity fixture"
            };
            rule.RuleScripts.Add(new RuleNseScript
            {
                RuleId = rule.Id,
                NseScriptId = script.Id,
                ScanCheckId = script.Id
            });
            return rule;
        }).ToList();
        rules.Add(new Rule
        {
            Id = "parity-xpath",
            Title = "XML evidence",
            Description = "XPath survives the execution boundary",
            Enabled = true,
            Severity = Severity.Medium,
            Weight = 2,
            ParserType = ParserType.StructuredXml,
            MatcherType = MatcherType.XPath,
            MatcherExpression = "//host",
            Remediation = "Parity fixture"
        });
        var oldFindings = await ruleEngine.EvaluateAsync(
            legacyScan, target, rules, legacyResult);
        var newFindings = await ruleEngine.EvaluateAsync(
            strategyScan, target, rules, productionResult);
        Assert.NotEmpty(oldFindings);
        Assert.Equal(
            oldFindings.Select(finding => (finding.RuleId, finding.SourceScript,
                finding.Evidence, finding.ScoreContribution, finding.Severity)),
            newFindings.Select(finding => (finding.RuleId, finding.SourceScript,
                finding.Evidence, finding.ScoreContribution, finding.Severity)));
        var oldRisk = scoring.CalculateRiskScore(oldFindings);
        var newRisk = scoring.CalculateRiskScore(newFindings);
        Assert.Equal(oldRisk, newRisk);
        Assert.Equal(
            scoring.CalculateOverallSeverity(oldFindings, oldRisk),
            scoring.CalculateOverallSeverity(newFindings, newRisk));

        return composite;
    }

    private static ComparableResult ToComparableResult(ScanScriptExecutionResult result) =>
        new(
            result.NseScriptId,
            result.ScriptName,
            result.NormalizedOutput,
            result.ExitCode,
            result.ResultStatus);

    private static ComparableResult ToComparableResult(CheckExecutionResult result) =>
        new(
            result.CheckId,
            result.CheckName,
            result.NormalizedOutput,
            result.ExitCode,
            result.ResultStatus);

    private static StrategyFixture CreateFixture()
    {
        var mySqlProbe = new DeterministicMySqlProbe();
        var msSqlProbe = new DeterministicMsSqlProbe();
        var browserProbe = new DeterministicBrowserProbe();
        var oracleProbe = new DeterministicOracleProbe();
        var nmapRuntime = new NmapScannerRuntime(
            Options.Create(new ScannerOptions
            {
                ExecutablePath = "nmap-does-not-exist",
                TimeoutSeconds = 1,
                UseSampleOutputWhenUnavailable = true
            }),
            mySqlProbe,
            NullLogger<NmapScannerRuntime>.Instance);
        var compatibilityRuntime = new ScriptCompatibilityScannerRuntime(nmapRuntime);
        var msSqlRuntime = new MsSqlScannerRuntime(
            compatibilityRuntime,
            msSqlProbe,
            browserProbe);
        IScannerRuntime legacyRuntime = new OracleScannerRuntime(msSqlRuntime, oracleProbe);
        IScanExecutorStrategy[] strategies =
        [
            new NmapNseScanExecutorStrategy(compatibilityRuntime),
            new MySqlNativeScanExecutorStrategy(nmapRuntime),
            new MsSqlNativeScanExecutorStrategy(msSqlProbe, browserProbe),
            new OracleNativeScanExecutorStrategy(oracleProbe)
        ];
        var resolver = new ScanExecutorStrategyResolver(strategies);

        return new StrategyFixture(
            legacyRuntime,
            new NseScriptScanCheckRouter(),
            resolver,
            new ScanExecutionOrchestrator(resolver));
    }

    private static Target CreateTarget(DatabaseEngine engine) =>
        new()
        {
            Name = $"Demo {engine}",
            Host = $"demo-{engine.ToApiString()}-target",
            Port = engine switch
            {
                DatabaseEngine.PostgreSql => 5432,
                DatabaseEngine.MySql => 3306,
                DatabaseEngine.MsSql => 1433,
                DatabaseEngine.Oracle => 1521,
                _ => 9999
            },
            Engine = engine
        };

    private static NseScript CreateScript(
        string name,
        string supportedEngine,
        bool requiresCredentials = false) =>
        new()
        {
            Id = $"check-{name.ToLowerInvariant()}",
            NseScriptName = name,
            DisplayName = name,
            Description = name,
            SupportedEngines = supportedEngine,
            DefaultEnabled = true,
            RequiresCredentials = requiresCredentials,
            ParserType = ParserType.GenericOutput,
            MatcherType = MatcherType.Contains,
            ApprovalStatus = ScriptApprovalStatus.Approved
        };

    private sealed record ComparableResult(
        string? Id,
        string Name,
        string NormalizedOutput,
        int? ExitCode,
        ProbeResultStatus? Status);

    private sealed record StrategyFixture(
        IScannerRuntime LegacyRuntime,
        INseScriptScanCheckRouter Router,
        ScanExecutorStrategyResolver Resolver,
        ScanExecutionOrchestrator Orchestrator);

    private sealed class DeterministicMySqlProbe : IMySqlEmptyPasswordProbe
    {
        public Task<ScanScriptExecutionResult> RunAsync(
            Target target,
            NseScript script,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateProbeResult(script, "mysql84", ProbeResultStatus.Passed));
    }

    private sealed class DeterministicMsSqlProbe : IMsSqlAuthenticatedProbe
    {
        public Task<IReadOnlyList<ScanScriptExecutionResult>> RunAsync(
            Target target,
            IReadOnlyCollection<NseScript> scripts,
            DatabaseAuthentication authentication,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ScanScriptExecutionResult>>(
                scripts.Select(script => CreateProbeResult(
                    script,
                    "mssql-auth",
                    ProbeResultStatus.Passed)).ToArray());
        }
    }

    private sealed class DeterministicBrowserProbe : IMsSqlBrowserProbe
    {
        public Task<ScanScriptExecutionResult> RunAsync(
            Target target,
            NseScript script,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CreateProbeResult(
                script,
                "mssql-browser",
                ProbeResultStatus.Passed));
        }
    }

    private sealed class DeterministicOracleProbe : IOracleAuthenticatedProbe
    {
        public Task<IReadOnlyList<ScanScriptExecutionResult>> RunAsync(
            Target target,
            IReadOnlyCollection<NseScript> scripts,
            DatabaseAuthentication authentication,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ScanScriptExecutionResult>>(
                scripts.Select(script => CreateProbeResult(
                    script,
                    "oracle-auth",
                    ProbeResultStatus.Passed)).ToArray());
        }
    }

    private static ScanScriptExecutionResult CreateProbeResult(
        NseScript script,
        string runner,
        ProbeResultStatus status)
    {
        var output = $"status={status.ToString().ToLowerInvariant()}; runner={runner}";
        var rawXml = new XElement(
            "script",
            new XAttribute("id", script.NseScriptName),
            new XAttribute("runner", runner),
            new XAttribute("output", output))
            .ToString(SaveOptions.DisableFormatting);
        return new ScanScriptExecutionResult(
            script.Id,
            script.NseScriptName,
            rawXml,
            output,
            0,
            status);
    }

    private sealed class StaticStrategy(string executorType) : IScanExecutorStrategy
    {
        public string ExecutorType { get; } = executorType;

        public Task<ScanExecutorResult> ExecuteAsync(
            ScanExecutionContext context,
            IReadOnlyCollection<ScanCheckDefinition> checks,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ScanExecutorResult(string.Empty, [], []));
    }
}
