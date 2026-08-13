using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Application.Services;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using Xunit;

namespace DbScanner.Application.Tests;

public sealed class ScanWorkflowTests
{
    [Fact]
    public async Task CreateScanAsync_PersistsPendingScanAndPublishesJob()
    {
        var target = new Target
        {
            Id = Guid.NewGuid(),
            Name = "Demo PostgreSQL",
            Host = "demo-postgres-target",
            Port = 5432,
            Engine = DatabaseEngine.PostgreSql
        };

        var targetRepository = new StubTargetRepository(target);
        var scanRepository = new InMemoryScanRepository();
        var scopeRepository = new StubScanScopeRepository(true);
        var publisher = new StubScanJobPublisher();
        var service = new ScanService(
            targetRepository,
            scanRepository,
            scopeRepository,
            publisher,
            new StubCredentialProtector());

        var scan = await service.CreateScanAsync(target.Id, new[] { "banner" }, CancellationToken.None);

        Assert.Equal(ScanStatus.Pending, scan.Status);
        Assert.Single(scanRepository.Scans);
        Assert.Equal(scan.Id, publisher.PublishedScanId);
        Assert.Equal(target.Id, publisher.PublishedTargetId);
        Assert.Equal(new[] { "banner" }, publisher.PublishedScriptIds);
    }

    [Fact]
    public async Task CreateScanAsync_PublishesValidatedMsSqlAuthentication()
    {
        var target = new Target
        {
            Id = Guid.NewGuid(),
            Name = "Demo Microsoft SQL Server",
            Host = "demo-mssql-target",
            Port = 1433,
            Engine = DatabaseEngine.MsSql,
            ProtectedCredentials = "protected-target-credentials"
        };
        var publisher = new StubScanJobPublisher();
        var authentication = new DatabaseAuthentication(
            "DbScannerDemo",
            "dbscanner_scanner",
            "test-only-password",
            Encrypt: true,
            TrustServerCertificate: false,
            ConnectionTimeoutSeconds: 5);
        var service = new ScanService(
            new StubTargetRepository(target),
            new InMemoryScanRepository(),
            new StubScanScopeRepository(true),
            publisher,
            new StubCredentialProtector(authentication));

        await service.CreateScanAsync(target.Id, ["mssql-version"], CancellationToken.None);

        Assert.Equal(authentication, publisher.PublishedAuthentication);
    }

    [Fact]
    public async Task CreateScanAsync_PublishesStoredOracleAuthenticationThroughCommonScanFlow()
    {
        var target = new Target
        {
            Id = Guid.NewGuid(),
            Name = "Demo Oracle Database",
            Host = "demo-oracle-target",
            Port = 1521,
            Engine = DatabaseEngine.Oracle,
            ProtectedCredentials = "protected-target-credentials"
        };
        var publisher = new StubScanJobPublisher();
        var authentication = new DatabaseAuthentication(
            "FREEPDB1",
            "dbscanner_scanner",
            "test-only-password",
            ConnectionTimeoutSeconds: 5);
        var service = new ScanService(
            new StubTargetRepository(target),
            new InMemoryScanRepository(),
            new StubScanScopeRepository(true),
            publisher,
            new StubCredentialProtector(authentication));

        await service.CreateScanAsync(
            target.Id,
            ["oracle-auth-version"],
            CancellationToken.None);

        Assert.Equal(authentication, publisher.PublishedAuthentication);
    }

    [Fact]
    public async Task CreateScanAsync_DoesNotTransportStoredCredentialsForUnsupportedEngine()
    {
        var target = new Target
        {
            Id = Guid.NewGuid(),
            Name = "Demo PostgreSQL",
            Host = "demo-postgres-target",
            Port = 5432,
            Engine = DatabaseEngine.PostgreSql,
            ProtectedCredentials = "protected-target-credentials"
        };
        var publisher = new StubScanJobPublisher();
        var service = new ScanService(
            new StubTargetRepository(target),
            new InMemoryScanRepository(),
            new StubScanScopeRepository(true),
            publisher,
            new StubCredentialProtector());

        await service.CreateScanAsync(target.Id, ["banner"], CancellationToken.None);

        Assert.Null(publisher.PublishedAuthentication);
    }

    [Fact]
    public async Task ProcessAsync_StoresArtifactsAndMarksScanScanned()
    {
        var target = new Target
        {
            Id = Guid.NewGuid(),
            Name = "Demo PostgreSQL",
            Host = "demo-postgres-target",
            Port = 5432,
            Engine = DatabaseEngine.PostgreSql
        };

        var scan = new Scan
        {
            Id = Guid.NewGuid(),
            TargetId = target.Id,
            Target = target,
            Status = ScanStatus.Scanning
        };

        var rule = new Rule
        {
            Id = "pg_service_detected",
            Title = "PostgreSQL service detected",
            Description = "PostgreSQL service is detected on the target.",
            Engine = DatabaseEngine.PostgreSql,
            Severity = Severity.Info,
            Weight = 0,
            ParserType = ParserType.StructuredXml,
            MatcherType = MatcherType.Contains,
            MatcherExpression = "postgresql",
            Remediation = "Confirm the PostgreSQL exposure is expected."
        };
        rule.RuleScripts.Add(new RuleNseScript { RuleId = rule.Id, NseScriptId = "banner" });

        var script = new NseScript
        {
            Id = "banner",
            NseScriptName = "banner",
            DisplayName = "Banner",
            Description = "Banner",
            SupportedEngines = "postgresql",
            DefaultEnabled = true,
            ParserType = ParserType.GenericOutput,
            MatcherType = MatcherType.Contains,
            ApprovalStatus = ScriptApprovalStatus.Approved
        };

        var executionResult = new ScanExecutionResult(
            "<script id=\"banner\" output=\"postgresql 16\" />",
            "service=postgresql; product=PostgreSQL DB; version=16.3",
            [
                new ScanScriptExecutionResult("banner", "banner", "<script id=\"banner\" output=\"postgresql 16\" />", "postgresql 16", 0)
            ]);

        var finding = new Finding
        {
            ScanId = scan.Id,
            TargetId = target.Id,
            RuleId = rule.Id,
            Title = rule.Title,
            Severity = rule.Severity,
            Status = FindingStatus.Open,
            Evidence = executionResult.StructuredOutput,
            Remediation = rule.Remediation,
            SourceScript = "banner",
            ScoreContribution = 1
        };

        var scanRepository = new InMemoryScanRepository(scan);
        var scriptRepository = new StubNseScriptRepository(script);
        var ruleRepository = new StubRuleRepository(rule);
        var runtime = new StubScannerRuntime(executionResult);
        var engine = new StubRuleEngine([finding]);
        var scoring = new ScoringService();
        var service = new ScanProcessingService(scanRepository, scriptRepository, ruleRepository, runtime, engine, scoring);

        await service.ProcessAsync(scan.Id);

        var storedScan = Assert.Single(scanRepository.Scans);
        Assert.Equal(ScanStatus.Scanned, storedScan.Status);
        Assert.Equal(1, storedScan.RiskScore);
        Assert.Equal(Severity.Low, storedScan.OverallSeverity);
        Assert.Single(storedScan.ScriptResults);
        Assert.Single(storedScan.Findings);
    }

    [Fact]
    public async Task ProcessAsync_MsSqlAuthenticatedResultCreatesFindingEndToEnd()
    {
        var target = new Target
        {
            Id = Guid.NewGuid(),
            Name = "Demo Microsoft SQL Server",
            Host = "demo-mssql-target",
            Port = 1433,
            Engine = DatabaseEngine.MsSql
        };
        var scan = new Scan
        {
            Id = Guid.NewGuid(),
            TargetId = target.Id,
            Target = target,
            Status = ScanStatus.Scanning
        };
        var script = new NseScript
        {
            Id = "mssql-auth-xp-cmdshell",
            NseScriptName = "mssql-auth-xp-cmdshell",
            DisplayName = "Microsoft SQL Server xp_cmdshell",
            Description = "Reads the configuration without executing it.",
            SupportedEngines = "mssql",
            DefaultEnabled = true,
            RequiresCredentials = true,
            ParserType = ParserType.GenericOutput,
            MatcherType = MatcherType.Contains,
            ApprovalStatus = ScriptApprovalStatus.Approved
        };
        var rule = new Rule
        {
            Id = "mssql_xp_cmdshell_enabled",
            Title = "xp_cmdshell enabled",
            Description = "The xp_cmdshell configuration is enabled.",
            Engine = DatabaseEngine.MsSql,
            Severity = Severity.Critical,
            Weight = 4,
            Enabled = true,
            ParserType = ParserType.GenericOutput,
            MatcherType = MatcherType.Contains,
            MatcherExpression = "status=failed",
            Remediation = "Disable xp_cmdshell."
        };
        rule.RuleScripts.Add(new RuleNseScript
        {
            RuleId = rule.Id,
            NseScriptId = script.Id
        });
        var execution = new ScanExecutionResult(
            "<script id=\"mssql-auth-xp-cmdshell\" />",
            "mssql-auth-xp-cmdshell=status=failed",
            [
                new ScanScriptExecutionResult(
                    script.Id,
                    script.NseScriptName,
                    null,
                    "check=xp_cmdshell; status=failed; evidence=enabled",
                    0,
                    ProbeResultStatus.Failed)
            ]);
        var repository = new InMemoryScanRepository(scan);
        var scoring = new ScoringService();
        var service = new ScanProcessingService(
            repository,
            new StubNseScriptRepository(script),
            new StubRuleRepository(rule),
            new StubScannerRuntime(execution),
            new RuleEngine(scoring),
            scoring);

        await service.ProcessAsync(
            scan.Id,
            [script.Id],
            new DatabaseAuthentication(
                "DbScannerDemo",
                "dbscanner_scanner",
                "test-only-password"),
            CancellationToken.None);

        var stored = Assert.Single(repository.Scans);
        Assert.Equal(ScanStatus.Scanned, stored.Status);
        Assert.Equal(ProbeResultStatus.Failed, Assert.Single(stored.ScriptResults).ResultStatus);
        var finding = Assert.Single(stored.Findings);
        Assert.Equal(rule.Id, finding.RuleId);
        Assert.DoesNotContain("test-only-password", finding.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_OracleDiscoveryAndAuthenticatedResultCreateFindingEndToEnd()
    {
        var target = new Target
        {
            Id = Guid.NewGuid(),
            Name = "Demo Oracle Database",
            Host = "demo-oracle-target",
            Port = 1521,
            Engine = DatabaseEngine.Oracle
        };
        var scan = new Scan
        {
            Id = Guid.NewGuid(),
            TargetId = target.Id,
            Target = target,
            Status = ScanStatus.Scanning
        };
        var discoveryScript = new NseScript
        {
            Id = "oracle-tns-version",
            NseScriptName = "oracle-tns-version",
            DisplayName = "Oracle Listener and TNS Version",
            Description = "Reads safe Oracle Listener metadata.",
            SupportedEngines = "oracle",
            DefaultEnabled = true,
            ParserType = ParserType.StructuredXml,
            MatcherType = MatcherType.Contains,
            ApprovalStatus = ScriptApprovalStatus.Approved
        };
        var authenticatedScript = new NseScript
        {
            Id = "oracle-auth-public-privileges",
            NseScriptName = "oracle-auth-public-privileges",
            DisplayName = "Oracle PUBLIC Risky Privileges",
            Description = "Reads PUBLIC grants without changing the database.",
            SupportedEngines = "oracle",
            DefaultEnabled = true,
            RequiresCredentials = true,
            ParserType = ParserType.GenericOutput,
            MatcherType = MatcherType.Contains,
            ApprovalStatus = ScriptApprovalStatus.Approved
        };
        var rule = new Rule
        {
            Id = "oracle_public_risky_privileges",
            Title = "Risky Oracle privileges granted to PUBLIC",
            Description = "PUBLIC has a high-risk package privilege.",
            Engine = DatabaseEngine.Oracle,
            Severity = Severity.High,
            Weight = 3,
            Enabled = true,
            ParserType = ParserType.GenericOutput,
            MatcherType = MatcherType.Contains,
            MatcherExpression = "status=failed",
            Remediation = "Revoke unnecessary PUBLIC grants."
        };
        rule.RuleScripts.Add(new RuleNseScript
        {
            RuleId = rule.Id,
            NseScriptId = authenticatedScript.Id
        });
        var execution = new ScanExecutionResult(
            "<script id=\"oracle-tns-version\" output=\"Oracle Database 23ai\" />",
            "state=open; service=oracle-tns; oracle-auth-public-privileges=status=failed",
            [
                new ScanScriptExecutionResult(
                    discoveryScript.Id,
                    discoveryScript.NseScriptName,
                    null,
                    "Oracle Database 23ai Listener",
                    0),
                new ScanScriptExecutionResult(
                    authenticatedScript.Id,
                    authenticatedScript.NseScriptName,
                    null,
                    "check=public-privileges; status=failed; evidence=risky_public_privilege_grants=1",
                    0,
                    ProbeResultStatus.Failed)
            ]);
        var repository = new InMemoryScanRepository(scan);
        var scoring = new ScoringService();
        var service = new ScanProcessingService(
            repository,
            new StubNseScriptRepository(discoveryScript, authenticatedScript),
            new StubRuleRepository(rule),
            new StubScannerRuntime(execution),
            new RuleEngine(scoring),
            scoring);

        await service.ProcessAsync(
            scan.Id,
            [discoveryScript.Id, authenticatedScript.Id],
            new DatabaseAuthentication(
                "FREEPDB1",
                "dbscanner_scanner",
                "oracle-test-password"),
            CancellationToken.None);

        var stored = Assert.Single(repository.Scans);
        Assert.Equal(ScanStatus.Scanned, stored.Status);
        Assert.Equal(2, stored.ScriptResults.Count);
        Assert.Contains(
            stored.ScriptResults,
            result => result.ScriptName == discoveryScript.NseScriptName);
        var finding = Assert.Single(stored.Findings);
        Assert.Equal(rule.Id, finding.RuleId);
        Assert.DoesNotContain("oracle-test-password", finding.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessAsync_MarksScanTimeoutWhenRuntimeTimesOut()
    {
        var target = new Target
        {
            Id = Guid.NewGuid(),
            Name = "Demo PostgreSQL",
            Host = "demo-postgres-target",
            Port = 5432,
            Engine = DatabaseEngine.PostgreSql
        };

        var scan = new Scan
        {
            Id = Guid.NewGuid(),
            TargetId = target.Id,
            Target = target,
            Status = ScanStatus.Scanning
        };

        var scanRepository = new InMemoryScanRepository(scan);
        var service = new ScanProcessingService(
            scanRepository,
            new StubNseScriptRepository(),
            new StubRuleRepository(),
            new ThrowingScannerRuntime(new TimeoutException("scan timed out")),
            new StubRuleEngine([]),
            new ScoringService());

        await service.ProcessAsync(scan.Id);

        var storedScan = Assert.Single(scanRepository.Scans);
        Assert.Equal(ScanStatus.Timeout, storedScan.Status);
        Assert.Equal("scan timed out", storedScan.ErrorMessage);
    }

    [Fact]
    public async Task TryMarkScanningAsync_ReturnsFalseWhenScanIsAlreadyScanning()
    {
        var scan = new Scan
        {
            Id = Guid.NewGuid(),
            Status = ScanStatus.Scanning
        };

        var repository = new InMemoryScanRepository(scan);

        var markedScanning = await repository.TryMarkScanningAsync(scan.Id);

        Assert.False(markedScanning);
        Assert.Equal(ScanStatus.Scanning, scan.Status);
    }

    [Fact]
    public async Task DeleteScanAsync_DeletesCancelledScan()
    {
        var target = new Target
        {
            Id = Guid.NewGuid(),
            Name = "Demo PostgreSQL",
            Host = "demo-postgres-target",
            Port = 5432,
            Engine = DatabaseEngine.PostgreSql
        };

        var scan = new Scan
        {
            Id = Guid.NewGuid(),
            TargetId = target.Id,
            Status = ScanStatus.Cancelled
        };

        var repository = new InMemoryScanRepository(scan);
        var service = new ScanService(
            new StubTargetRepository(target),
            repository,
            new StubScanScopeRepository(true),
            new StubScanJobPublisher(),
            new StubCredentialProtector());

        var deleted = await service.DeleteScanAsync(scan.Id);

        Assert.True(deleted);
        Assert.Empty(repository.Scans);
    }

    private sealed class StubTargetRepository(params Target[] targets) : ITargetRepository
    {
        private readonly List<Target> _targets = [.. targets];

        public Task<IReadOnlyList<Target>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Target>>(_targets);

        public Task<Target?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_targets.SingleOrDefault(target => target.Id == id));

        public Task<Target?> FindByEndpointAsync(
            string normalizedHost,
            int port,
            DatabaseEngine engine,
            Guid? excludeTargetId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_targets.FirstOrDefault(target =>
                target.Id != excludeTargetId
                && target.Host.Equals(normalizedHost, StringComparison.OrdinalIgnoreCase)
                && target.Port == port
                && target.Engine == engine));

        public Task AddAsync(Target target, CancellationToken cancellationToken = default)
        {
            _targets.Add(target);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Target target, CancellationToken cancellationToken = default)
        {
            _targets.RemoveAll(item => item.Id == target.Id);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubScanScopeRepository(bool isAllowed) : IScanScopeRepository
    {
        public Task<IReadOnlyList<ScanScopeAllowlist>> ListEnabledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScanScopeAllowlist>>([]);

        public Task<IReadOnlyList<ScanScopeAllowlist>> ListAsync(string? environment = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ScanScopeAllowlist>>([]);
        public Task<ScanScopeAllowlist?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<ScanScopeAllowlist?>(null);
        public Task AddAsync(ScanScopeAllowlist entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(ScanScopeAllowlist entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> IsAllowedAsync(string host, int port, string environment, DatabaseEngine engine, CancellationToken cancellationToken = default) =>
            Task.FromResult(isAllowed);
    }

    private sealed class StubScanJobPublisher : IScanJobPublisher
    {
        public Guid? PublishedScanId { get; private set; }
        public Guid? PublishedTargetId { get; private set; }

        public IReadOnlyCollection<string>? PublishedScriptIds { get; private set; }
        public DatabaseAuthentication? PublishedAuthentication { get; private set; }

        public Task PublishScanRequestedAsync(Scan scan, Target target, CancellationToken cancellationToken = default) =>
            PublishScanRequestedAsync(scan, target, selectedNseScriptIds: null, cancellationToken);

        public Task PublishScanRequestedAsync(
            Scan scan,
            Target target,
            IReadOnlyCollection<string>? selectedNseScriptIds,
            CancellationToken cancellationToken)
            => PublishScanRequestedAsync(
                scan,
                target,
                selectedNseScriptIds,
                authentication: null,
                cancellationToken);

        public Task PublishScanRequestedAsync(
            Scan scan,
            Target target,
            IReadOnlyCollection<string>? selectedNseScriptIds,
            DatabaseAuthentication? authentication,
            CancellationToken cancellationToken)
        {
            PublishedScanId = scan.Id;
            PublishedTargetId = target.Id;
            PublishedScriptIds = selectedNseScriptIds;
            PublishedAuthentication = authentication;
            return Task.CompletedTask;
        }
    }

    private sealed class StubCredentialProtector(
        DatabaseAuthentication? authentication = null) : IScanCredentialProtector
    {
        public string Protect(DatabaseAuthentication value) => "protected-target-credentials";

        public DatabaseAuthentication Unprotect(string protectedCredentials) =>
            authentication
            ?? throw new InvalidOperationException("Credential unprotect was not expected.");
    }

    private sealed class InMemoryScanRepository(params Scan[] scans) : IScanRepository
    {
        public List<Scan> Scans { get; } = [.. scans];

        public Task<Scan?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scans.SingleOrDefault(scan => scan.Id == id));

        public Task<IReadOnlyList<Scan>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Scan>>(Scans.OrderByDescending(scan => scan.CreatedAt).ToArray());

        public Task<IReadOnlyList<Scan>> ListByTargetAsync(Guid targetId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Scan>>(Scans.Where(scan => scan.TargetId == targetId).ToArray());

        public Task<bool> HasActiveScanAsync(Guid targetId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scans.Any(scan => scan.TargetId == targetId && scan.Status is ScanStatus.Pending or ScanStatus.Scanning));

        public Task AddAsync(Scan scan, CancellationToken cancellationToken = default)
        {
            Scans.Add(scan);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Scan scan, CancellationToken cancellationToken = default)
        {
            Scans.Remove(scan);
            return Task.CompletedTask;
        }

        public Task<bool> TryMarkScanningAsync(Guid scanId, CancellationToken cancellationToken = default)
        {
            var scan = Scans.SingleOrDefault(item => item.Id == scanId);
            if (scan is null)
            {
                return Task.FromResult(false);
            }

            if (scan.Status == ScanStatus.Scanning)
            {
                return Task.FromResult(false);
            }

            if (scan.Status == ScanStatus.Pending)
            {
                scan.Status = ScanStatus.Scanning;
            }

            return Task.FromResult(scan.Status == ScanStatus.Scanning);
        }

        public Task IncrementRetryCountAsync(Guid scanId, CancellationToken cancellationToken = default)
        {
            var scan = Scans.SingleOrDefault(item => item.Id == scanId);
            if (scan is not null)
            {
                scan.RetryCount++;
            }

            return Task.CompletedTask;
        }

        public Task StoreExecutionArtifactsAsync(Guid scanId, IReadOnlyCollection<ScanScriptResult> scriptResults, IReadOnlyCollection<Finding> findings, int? riskScore, Severity? overallSeverity, ScanStatus status, string? errorMessage = null, CancellationToken cancellationToken = default)
        {
            var scan = Scans.Single(item => item.Id == scanId);
            scan.ScriptResults.Clear();
            scan.Findings.Clear();

            foreach (var scriptResult in scriptResults)
            {
                scan.ScriptResults.Add(scriptResult);
            }

            foreach (var finding in findings)
            {
                scan.Findings.Add(finding);
            }

            scan.RiskScore = riskScore;
            scan.OverallSeverity = overallSeverity;
            scan.Status = status;
            scan.ErrorMessage = errorMessage;
            scan.FinishedAt = DateTimeOffset.UtcNow;

            return Task.CompletedTask;
        }

        public Task UpdateStatusAsync(Guid scanId, ScanStatus status, string? errorMessage = null, CancellationToken cancellationToken = default)
        {
            var scan = Scans.Single(item => item.Id == scanId);
            scan.Status = status;
            scan.ErrorMessage = errorMessage;
            scan.FinishedAt = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }

        public void ClearTracking()
        {
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubNseScriptRepository(params NseScript[] scripts) : INseScriptRepository
    {
        private readonly IReadOnlyList<NseScript> _scripts = scripts;

        public Task<IReadOnlyList<NseScript>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_scripts);

        public Task<IReadOnlyList<NseScript>> ListInstalledForSyncAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NseScript>>(_scripts.Where(script => script.Source == NseScriptSource.Installed).ToArray());

        public Task<IReadOnlyList<NseScript>> ListApprovedByEngineAsync(DatabaseEngine engine, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NseScript>>(_scripts.Where(script =>
                script.ApprovalStatus == ScriptApprovalStatus.Approved
                && script.SupportedEngines.Contains(engine.ToString(), StringComparison.OrdinalIgnoreCase)).ToArray());

        public Task<IReadOnlyList<NseScript>> ListRunnableByIdsAsync(DatabaseEngine _, IReadOnlyCollection<string> scriptIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NseScript>>(_scripts.Where(script =>
                script.ApprovalStatus != ScriptApprovalStatus.Disabled
                && scriptIds.Contains(script.Id, StringComparer.OrdinalIgnoreCase)).ToArray());

        public Task<NseScript?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_scripts.SingleOrDefault(script => script.Id == id));

        public Task AddAsync(NseScript script, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Delete(NseScript script)
        {
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubRuleRepository(params Rule[] rules) : IRuleRepository
    {
        private readonly IReadOnlyList<Rule> _rules = rules;

        public Task<IReadOnlyList<Rule>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_rules);

        public Task<IReadOnlyList<Rule>> ListEnabledByEngineAsync(DatabaseEngine engine, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Rule>>(_rules.Where(rule => rule.Enabled && rule.Engine == engine).ToArray());

        public Task<Rule?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_rules.SingleOrDefault(rule => rule.Id == id));

        public Task AddAsync(Rule rule, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubScannerRuntime(ScanExecutionResult result) : IScannerRuntime
    {
        public Task<ScanExecutionResult> RunAsync(
            Scan scan,
            Target target,
            IReadOnlyCollection<NseScript> scripts,
            DatabaseAuthentication? authentication = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class ThrowingScannerRuntime(Exception exception) : IScannerRuntime
    {
        public Task<ScanExecutionResult> RunAsync(
            Scan scan,
            Target target,
            IReadOnlyCollection<NseScript> scripts,
            DatabaseAuthentication? authentication = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ScanExecutionResult>(exception);
    }

    private sealed class StubRuleEngine(IReadOnlyList<Finding> findings) : IRuleEngine
    {
        public Task<IReadOnlyList<Finding>> EvaluateAsync(Scan scan, Target target, IReadOnlyCollection<Rule> rules, ScanExecutionResult executionResult, CancellationToken cancellationToken = default) =>
            Task.FromResult(findings);
    }
}
