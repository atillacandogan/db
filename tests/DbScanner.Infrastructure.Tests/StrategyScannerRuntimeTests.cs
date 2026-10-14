using System.Xml.Linq;
using System.Xml.XPath;
using DbScanner.Application;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Infrastructure.Scanning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

public sealed class StrategyScannerRuntimeTests
{
    [Fact]
    public async Task Runtime_uses_orchestrator_and_preserves_rule_engine_result_contract()
    {
        var target = CreateTarget(DatabaseEngine.MsSql);
        var scan = new Scan { TargetId = target.Id, Target = target };
        var authentication = new DatabaseAuthentication(
            "DbScannerDemo",
            "scanner",
            "must-not-leak");
        var nmapCheck = CreateCheck("ms-sql-info", ScanExecutorTypes.NmapNse);
        var nativeCheck = CreateCheck("mssql-auth-version", ScanExecutorTypes.MsSqlNative);
        var router = new StubRouter([nmapCheck, nativeCheck]);
        var orchestrator = new RecordingOrchestrator(CreateCompositeResult());
        var runtime = CreateRuntime(router, orchestrator);

        var result = await runtime.RunAsync(
            scan,
            target,
            [CreateScript("ms-sql-info")],
            authentication);

        Assert.Equal(1, router.InvocationCount);
        Assert.Same(scan, orchestrator.Context!.Scan);
        Assert.Same(target, orchestrator.Context.Target);
        Assert.Same(authentication, orchestrator.Context.Authentication);
        Assert.Equal([ScanExecutorTypes.NmapNse], orchestrator.Context.RequiredExecutorTypes);
        Assert.Equal([nmapCheck, nativeCheck], orchestrator.Checks);
        Assert.Equal("<nmaprun><host /></nmaprun>", result.RawXml);
        Assert.Equal("state=open; native=passed", result.StructuredOutput);
        Assert.Equal(2, result.ScriptResults.Count);
        Assert.Equal("ms-sql-info", result.ScriptResults[0].NseScriptId);
        Assert.Equal("mssql-auth-version", result.ScriptResults[1].NseScriptId);
        Assert.DoesNotContain(authentication.Password, result.StructuredOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_check_plan_requires_nmap_baseline_execution()
    {
        var router = new StubRouter([]);
        var orchestrator = new RecordingOrchestrator(new CompositeScanExecutionResult(
            "state=open",
            [],
            [new ScanExecutionArtifact(
                ScanExecutorTypes.NmapNse,
                "<nmaprun><host /></nmaprun>",
                ScanPayloadFormat.Xml)]));
        var runtime = CreateRuntime(router, orchestrator);
        var target = CreateTarget(DatabaseEngine.PostgreSql);

        var result = await runtime.RunAsync(
            new Scan { TargetId = target.Id, Target = target },
            target,
            []);

        Assert.Empty(orchestrator.Checks!);
        Assert.Equal([ScanExecutorTypes.NmapNse], orchestrator.Context!.RequiredExecutorTypes);
        Assert.Equal("<nmaprun><host /></nmaprun>", result.RawXml);
    }

    [Fact]
    public async Task MySql_compatibility_plan_does_not_schedule_a_second_nmap_baseline()
    {
        var mySqlCheck = CreateCheck("mysql-empty-password", ScanExecutorTypes.MySqlNative);
        var orchestrator = new RecordingOrchestrator(new CompositeScanExecutionResult(
            "state=open",
            [],
            [new ScanExecutionArtifact(
                ScanExecutorTypes.MySqlNative,
                "<nmaprun><host /></nmaprun>",
                ScanPayloadFormat.Xml)]));
        var runtime = CreateRuntime(
            new StubRouter([mySqlCheck]),
            orchestrator);
        var target = CreateTarget(DatabaseEngine.MySql);

        await runtime.RunAsync(
            new Scan { TargetId = target.Id, Target = target },
            target,
            [CreateScript("mysql-empty-password")]);

        Assert.Empty(orchestrator.Context!.RequiredExecutorTypes!);
    }

    [Fact]
    public async Task Multiple_xml_artifacts_are_merged_without_breaking_xpath_evidence()
    {
        var composite = new CompositeScanExecutionResult(
            "merged",
            [],
            [
                new ScanExecutionArtifact(
                    ScanExecutorTypes.NmapNse,
                    "<nmaprun><script id=\"nmap-check\" /></nmaprun>",
                    ScanPayloadFormat.Xml),
                new ScanExecutionArtifact(
                    ScanExecutorTypes.ExternalApi,
                    "<response><script id=\"api-check\" /></response>",
                    ScanPayloadFormat.Xml)
            ]);
        var runtime = CreateRuntime(
            new StubRouter([]),
            new RecordingOrchestrator(composite));
        var target = CreateTarget(DatabaseEngine.PostgreSql);

        var result = await runtime.RunAsync(
            new Scan { TargetId = target.Id, Target = target },
            target,
            []);
        var document = XDocument.Parse(result.RawXml);

        Assert.Equal("dbscanner-execution", document.Root!.Name.LocalName);
        Assert.Equal(2, document.XPathSelectElements("//script").Count());
    }

    [Fact]
    public async Task Timeout_and_requested_cancellation_propagate_unchanged()
    {
        var target = CreateTarget(DatabaseEngine.PostgreSql);
        var scan = new Scan { TargetId = target.Id, Target = target };
        var timeoutRuntime = CreateRuntime(
            new StubRouter([]),
            new RecordingOrchestrator(new TimeoutException("strategy timed out")));

        var timeout = await Assert.ThrowsAsync<TimeoutException>(() =>
            timeoutRuntime.RunAsync(scan, target, []));
        Assert.Equal("strategy timed out", timeout.Message);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelledRuntime = CreateRuntime(
            new StubRouter([]),
            new RecordingOrchestrator(new OperationCanceledException(cancellation.Token)));

        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancelledRuntime.RunAsync(scan, target, [], null, cancellation.Token));
        Assert.Equal(cancellation.Token, cancelled.CancellationToken);
    }

    [Fact]
    public void Infrastructure_dependency_injection_resolves_strategy_runtime_and_all_executors()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddDbScannerApplication()
            .AddDbScannerInfrastructure(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:dbscanner"] = "Host=localhost;Database=test;Username=test"
                }).Build());
        using var provider = services.BuildServiceProvider();

        var runtime = provider.GetRequiredService<IScannerRuntime>();
        var resolver = provider.GetRequiredService<IScanExecutorStrategyResolver>();

        Assert.IsType<StrategyScannerRuntime>(runtime);
        Assert.Null(provider.GetService<MsSqlScannerRuntime>());
        Assert.Null(provider.GetService<OracleScannerRuntime>());
        Assert.Equal(
            [
                ScanExecutorTypes.MsSqlNative,
                ScanExecutorTypes.MySqlNative,
                ScanExecutorTypes.NmapNse,
                ScanExecutorTypes.OracleNative
            ],
            resolver.RegisteredExecutorTypes);
    }

    private static StrategyScannerRuntime CreateRuntime(
        INseScriptScanCheckRouter router,
        IScanExecutionOrchestrator orchestrator) =>
        new(router, orchestrator);

    [Fact]
    public void Removed_rollback_flag_fails_instead_of_silently_changing_scan_behavior()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbScannerInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:dbscanner"] = "Host=localhost;Database=test;Username=test",
                ["Scanner:UseStrategyOrchestrator"] = "false"
            })
            .Build());
        using var provider = services.BuildServiceProvider();

        var error = Assert.Throws<InvalidOperationException>(() =>
            _ = provider.GetRequiredService<IOptions<ScannerOptions>>().Value);

        Assert.Contains("no longer supported", error.Message);
    }

    private static CompositeScanExecutionResult CreateCompositeResult() =>
        new(
            "state=open; native=passed",
            [
                new CheckExecutionResult(
                    "ms-sql-info",
                    "ms-sql-info",
                    ScanExecutorTypes.NmapNse,
                    "<script id=\"ms-sql-info\" />",
                    ScanPayloadFormat.Xml,
                    "state=open",
                    0),
                new CheckExecutionResult(
                    "mssql-auth-version",
                    "mssql-auth-version",
                    ScanExecutorTypes.MsSqlNative,
                    null,
                    ScanPayloadFormat.Xml,
                    "status=passed",
                    0,
                    ProbeResultStatus.Passed)
            ],
            [new ScanExecutionArtifact(
                ScanExecutorTypes.NmapNse,
                "<nmaprun><host /></nmaprun>",
                ScanPayloadFormat.Xml)]);

    private static Target CreateTarget(DatabaseEngine engine) =>
        new()
        {
            Name = engine.ToDisplayName(),
            Host = "db.internal",
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

    private static NseScript CreateScript(string id) =>
        new()
        {
            Id = id,
            NseScriptName = id,
            DisplayName = id,
            Description = id,
            SupportedEngines = "generic"
        };

    private static ScanCheckDefinition CreateCheck(string id, string executorType) =>
        new(
            id,
            id,
            id,
            id,
            executorType,
            [DatabaseEngine.Generic],
            null,
            true,
            false,
            false,
            null,
            ParserType.GenericOutput,
            MatcherType.Contains,
            null,
            Severity.Info,
            0,
            null,
            null,
            CheckApprovalStatus.Approved);

    private sealed class StubRouter(
        IReadOnlyList<ScanCheckDefinition> routedChecks) : INseScriptScanCheckRouter
    {
        public int InvocationCount { get; private set; }

        public ScanCheckDefinition Route(NseScript script) =>
            throw new NotSupportedException();

        public IReadOnlyList<ScanCheckDefinition> Route(IReadOnlyCollection<NseScript> scripts)
        {
            InvocationCount++;
            return routedChecks;
        }
    }

    private sealed class RecordingOrchestrator : IScanExecutionOrchestrator
    {
        private readonly CompositeScanExecutionResult? result;
        private readonly Exception? exception;

        public RecordingOrchestrator(CompositeScanExecutionResult result) =>
            this.result = result;

        public RecordingOrchestrator(Exception exception) =>
            this.exception = exception;

        public int InvocationCount { get; private set; }
        public ScanExecutionContext? Context { get; private set; }
        public IReadOnlyCollection<ScanCheckDefinition>? Checks { get; private set; }

        public Task<CompositeScanExecutionResult> ExecuteAsync(
            ScanExecutionContext context,
            IReadOnlyCollection<ScanCheckDefinition> checks,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            Context = context;
            Checks = checks;
            return exception is null
                ? Task.FromResult(result!)
                : Task.FromException<CompositeScanExecutionResult>(exception);
        }
    }
}
