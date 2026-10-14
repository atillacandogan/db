using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Application.Services;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DbScanner.Application.Tests;

public sealed class ScanExecutionStrategyTests
{
    [Fact]
    public void Resolver_returns_only_registered_strategy()
    {
        var expected = new StubStrategy(ScanExecutorTypes.NmapNse);
        var resolver = new ScanExecutorStrategyResolver([expected]);

        var actual = resolver.Resolve(ScanExecutorTypes.NmapNse);

        Assert.Same(expected, actual);
        Assert.Equal([ScanExecutorTypes.NmapNse], resolver.RegisteredExecutorTypes);
        Assert.Throws<InvalidOperationException>(() => resolver.Resolve(ScanExecutorTypes.ExternalApi));
    }

    [Fact]
    public void Resolver_rejects_duplicate_or_invalid_registrations()
    {
        var duplicateError = Assert.Throws<InvalidOperationException>(() =>
            new ScanExecutorStrategyResolver(
            [
                new StubStrategy(ScanExecutorTypes.NmapNse),
                new StubStrategy(ScanExecutorTypes.NmapNse)
            ]));
        var invalidError = Assert.Throws<InvalidOperationException>(() =>
            new ScanExecutorStrategyResolver([new StubStrategy("../../scanner")]));

        Assert.Contains("Multiple", duplicateError.Message, StringComparison.Ordinal);
        Assert.Contains("invalid executor type", invalidError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Orchestrator_groups_checks_and_merges_results_in_first_seen_executor_order()
    {
        var nmap = new StubStrategy(ScanExecutorTypes.NmapNse);
        var external = new StubStrategy(ScanExecutorTypes.ExternalApi);
        var orchestrator = new ScanExecutionOrchestrator(
            new ScanExecutorStrategyResolver([external, nmap]));
        var context = CreateContext();
        var checks = new[]
        {
            CreateCheck("nmap-a", ScanExecutorTypes.NmapNse),
            CreateCheck("api-a", ScanExecutorTypes.ExternalApi),
            CreateCheck("nmap-b", ScanExecutorTypes.NmapNse)
        };

        var result = await orchestrator.ExecuteAsync(context, checks);

        Assert.Single(nmap.Calls);
        Assert.Equal(["nmap-a", "nmap-b"], nmap.Calls[0].Checks.Select(item => item.Id));
        Assert.Same(context, nmap.Calls[0].Context);
        Assert.Single(external.Calls);
        Assert.Equal(["api-a"], external.Calls[0].Checks.Select(item => item.Id));
        Assert.Equal("nmap-nse:nmap-a,nmap-b; external-api:api-a", result.StructuredOutput);
        Assert.Equal(
            ["nmap-a", "nmap-b", "api-a"],
            result.CheckResults.Select(item => item.CheckId));
        Assert.Equal(
            [ScanExecutorTypes.NmapNse, ScanExecutorTypes.ExternalApi],
            result.Artifacts.Select(item => item.ExecutorType));
    }

    [Fact]
    public async Task Orchestrator_rejects_duplicate_check_ids_before_execution()
    {
        var strategy = new StubStrategy(ScanExecutorTypes.NmapNse);
        var orchestrator = new ScanExecutionOrchestrator(
            new ScanExecutorStrategyResolver([strategy]));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.ExecuteAsync(
                CreateContext(),
                [
                    CreateCheck("duplicate", ScanExecutorTypes.NmapNse),
                    CreateCheck("DUPLICATE", ScanExecutorTypes.NmapNse)
                ]));

        Assert.Contains("duplicate check id", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(strategy.Calls);
    }

    [Fact]
    public async Task Orchestrator_rejects_result_owned_by_another_executor()
    {
        var strategy = new StubStrategy(
            ScanExecutorTypes.NmapNse,
            (_, checks, _) => Task.FromResult(new ScanExecutorResult(
                "invalid",
                checks.Select(check => new CheckExecutionResult(
                    check.Id,
                    check.Name,
                    ScanExecutorTypes.ExternalApi,
                    null,
                    ScanPayloadFormat.Xml,
                    "invalid",
                    0)).ToArray(),
                [])));
        var orchestrator = new ScanExecutionOrchestrator(
            new ScanExecutorStrategyResolver([strategy]));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.ExecuteAsync(
                CreateContext(),
                [CreateCheck("check-a", ScanExecutorTypes.NmapNse)]));

        Assert.Contains("owned by", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Orchestrator_rejects_unrequested_check_results()
    {
        var strategy = new StubStrategy(
            ScanExecutorTypes.NmapNse,
            (_, _, _) => Task.FromResult(new ScanExecutorResult(
                "invalid",
                [new CheckExecutionResult(
                    "not-requested",
                    "Not requested",
                    ScanExecutorTypes.NmapNse,
                    null,
                    ScanPayloadFormat.Xml,
                    "invalid",
                    0)],
                [])));
        var orchestrator = new ScanExecutionOrchestrator(
            new ScanExecutorStrategyResolver([strategy]));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            orchestrator.ExecuteAsync(
                CreateContext(),
                [CreateCheck("requested", ScanExecutorTypes.NmapNse)]));

        Assert.Contains("unrequested check id", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Orchestrator_honors_cancellation_before_resolving_a_strategy()
    {
        var strategy = new StubStrategy(ScanExecutorTypes.NmapNse);
        var orchestrator = new ScanExecutionOrchestrator(
            new ScanExecutorStrategyResolver([strategy]));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            orchestrator.ExecuteAsync(
                CreateContext(),
                [CreateCheck("check-a", ScanExecutorTypes.NmapNse)],
                cancellation.Token));

        Assert.Empty(strategy.Calls);
    }

    [Fact]
    public async Task Empty_execution_plan_returns_an_empty_composite_without_resolving()
    {
        var orchestrator = new ScanExecutionOrchestrator(
            new ScanExecutorStrategyResolver([]));

        var result = await orchestrator.ExecuteAsync(CreateContext(), []);

        Assert.Empty(result.StructuredOutput);
        Assert.Empty(result.CheckResults);
        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task Required_executor_runs_first_even_when_its_check_group_is_empty()
    {
        var nmap = new StubStrategy(ScanExecutorTypes.NmapNse);
        var native = new StubStrategy(ScanExecutorTypes.MsSqlNative);
        var orchestrator = new ScanExecutionOrchestrator(
            new ScanExecutorStrategyResolver([native, nmap]));
        var baseContext = CreateContext();
        var context = baseContext with
        {
            RequiredExecutorTypes = [ScanExecutorTypes.NmapNse, ScanExecutorTypes.NmapNse]
        };

        var result = await orchestrator.ExecuteAsync(
            context,
            [CreateCheck("native-check", ScanExecutorTypes.MsSqlNative)]);

        var nmapCall = Assert.Single(nmap.Calls);
        Assert.Empty(nmapCall.Checks);
        Assert.Single(native.Calls);
        Assert.Equal(
            "nmap-nse:; mssql-native:native-check",
            result.StructuredOutput);
        Assert.Equal(
            [ScanExecutorTypes.NmapNse, ScanExecutorTypes.MsSqlNative],
            result.Artifacts.Select(artifact => artifact.ExecutorType));
    }

    [Fact]
    public void Application_dependency_injection_registers_strategy_services()
    {
        var services = new ServiceCollection();

        services.AddDbScannerApplication();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IScanExecutorStrategyResolver)
            && descriptor.ImplementationType == typeof(ScanExecutorStrategyResolver)
            && descriptor.Lifetime == ServiceLifetime.Singleton);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IScanExecutionOrchestrator)
            && descriptor.ImplementationType == typeof(ScanExecutionOrchestrator)
            && descriptor.Lifetime == ServiceLifetime.Singleton);
    }

    private static ScanExecutionContext CreateContext()
    {
        var target = new Target
        {
            Name = "Test database",
            Host = "db.internal",
            Port = 5432,
            Engine = DatabaseEngine.PostgreSql
        };
        var scan = new Scan { TargetId = target.Id, Target = target };
        return new ScanExecutionContext(scan, target);
    }

    private static ScanCheckDefinition CreateCheck(string id, string executorType) =>
        new(
            id,
            id,
            id,
            id,
            executorType,
            [DatabaseEngine.PostgreSql],
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

    private sealed class StubStrategy : IScanExecutorStrategy
    {
        private readonly Func<
            ScanExecutionContext,
            IReadOnlyCollection<ScanCheckDefinition>,
            CancellationToken,
            Task<ScanExecutorResult>> execute;

        public StubStrategy(
            string executorType,
            Func<
                ScanExecutionContext,
                IReadOnlyCollection<ScanCheckDefinition>,
                CancellationToken,
                Task<ScanExecutorResult>>? execute = null)
        {
            ExecutorType = executorType;
            this.execute = execute ?? CreateResult;
        }

        public string ExecutorType { get; }

        public List<ExecutionCall> Calls { get; } = [];

        public Task<ScanExecutorResult> ExecuteAsync(
            ScanExecutionContext context,
            IReadOnlyCollection<ScanCheckDefinition> checks,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new ExecutionCall(context, checks.ToArray()));
            return execute(context, checks, cancellationToken);
        }

        private Task<ScanExecutorResult> CreateResult(
            ScanExecutionContext context,
            IReadOnlyCollection<ScanCheckDefinition> checks,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var checkArray = checks.ToArray();
            return Task.FromResult(new ScanExecutorResult(
                $"{ExecutorType}:{string.Join(',', checkArray.Select(item => item.Id))}",
                checkArray.Select(check => new CheckExecutionResult(
                    check.Id,
                    check.Name,
                    ExecutorType,
                    $"payload:{check.Id}",
                    ScanPayloadFormat.Text,
                    $"normalized:{check.Id}",
                    0,
                    ProbeResultStatus.Passed)).ToArray(),
                [new ScanExecutionArtifact(
                    ExecutorType,
                    $"artifact:{ExecutorType}",
                    ScanPayloadFormat.Text)]));
        }
    }

    private sealed record ExecutionCall(
        ScanExecutionContext Context,
        IReadOnlyCollection<ScanCheckDefinition> Checks);
}
