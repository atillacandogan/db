using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Application.Services;
using DbScanner.Application.Exceptions;
using DbScanner.Contracts.Discovery;
using DbScanner.Contracts.Targets;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Domain.Services;
using Xunit;

namespace DbScanner.Application.Tests;

public sealed class DiscoveryServiceTests
{
    [Fact]
    public async Task CreateAsync_RejectsUnauthorizedScopeBeforePublishing()
    {
        var publisher = new StubDiscoveryPublisher();
        var service = CreateService(scopeValidator: new StubScopeValidator(authorized: false), publisher: publisher);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(
            new CreateDiscoveryCommand(
                "Unauthorized",
                "10.0.0.15",
                "prod",
                [DatabaseEngine.MySql],
                UseAuthorizedScope: false)));

        Assert.Equal(0, publisher.PublishCount);
    }

    [Fact]
    public async Task CreateAsync_QueuesOnlyAuthorizedTargetsFromMixedScope()
    {
        var repository = new InMemoryDiscoveryRepository();
        var publisher = new StubDiscoveryPublisher();
        var validations = new[]
        {
            new DiscoveryTargetValidationDto(
                "192.168.1.25",
                DiscoveryTargetValidationStatus.Authorized,
                "Authorized",
                ["192.168.1.25"]),
            new DiscoveryTargetValidationDto(
                "192.168.10.0/24",
                DiscoveryTargetValidationStatus.Authorized,
                "Authorized",
                ["192.168.10.1", "192.168.10.2"]),
            new DiscoveryTargetValidationDto(
                "db-server.company.local",
                DiscoveryTargetValidationStatus.DnsResolutionFailed,
                "DNS resolution failed",
                [])
        };
        var scopeValidator = new StubScopeValidator(authorized: true, validations);
        var service = CreateService(repository, scopeValidator: scopeValidator, publisher: publisher);

        var created = await service.CreateAsync(
            new CreateDiscoveryCommand(
                "Partial discovery",
                "192.168.1.25\n192.168.10.0/24\ndb-server.company.local",
                "demo",
                [DatabaseEngine.MySql],
                UseAuthorizedScope: false));

        Assert.Equal("192.168.1.25\n192.168.10.0/24", created.Scope);
        Assert.DoesNotContain("db-server.company.local", created.Scope);
        Assert.Equal(1, scopeValidator.ValidateCustomCount);
        Assert.Equal(1, publisher.PublishCount);
    }

    [Fact]
    public async Task CreateAsync_AutomaticModePlansAuthorizedScopeWithoutUserHostInput()
    {
        var repository = new InMemoryDiscoveryRepository();
        var publisher = new StubDiscoveryPublisher();
        var scopeValidator = new StubScopeValidator(authorized: true);
        var service = CreateService(repository, scopeValidator: scopeValidator, publisher: publisher);

        var created = await service.CreateAsync(
            new CreateDiscoveryCommand(
                "Automatic discovery",
                null,
                "demo",
                [DatabaseEngine.MySql],
                UseAuthorizedScope: true));

        Assert.Equal("All authorized endpoints", created.Scope);
        Assert.Equal(1, scopeValidator.PlanAuthorizedCount);
        Assert.Equal(0, scopeValidator.ValidateCustomCount);
        Assert.Equal(1, publisher.PublishCount);
    }

    [Fact]
    public async Task CreateLocalNetworkAsync_QueuesOnlyDetectedPrivateNetwork()
    {
        var repository = new InMemoryDiscoveryRepository();
        var publisher = new StubDiscoveryPublisher();
        var scopeValidator = new StubScopeValidator(authorized: true);
        var allowlist = new StubAllowlistService();
        var service = CreateService(
            repository,
            scopeValidator: scopeValidator,
            publisher: publisher,
            allowlistService: allowlist);

        var created = await service.CreateLocalNetworkAsync(
            "Office network",
            "prod",
            [DatabaseEngine.MySql, DatabaseEngine.PostgreSql],
            ["192.168.10.0/24"]);

        Assert.Contains("Local network: 192.168.10.0/24", created.Scope);
        Assert.Equal(1, scopeValidator.PlanLocalCount);
        Assert.Equal(0, scopeValidator.PlanAuthorizedCount);
        Assert.Empty(allowlist.Commands);
        Assert.Equal(1, publisher.PublishCount);
    }

    [Fact]
    public async Task ApproveAllowlistAsync_CreatesPrivateIpEntryFromLocalCandidate()
    {
        var job = CreateCompletedJob(DiscoveryResultStatus.Supported, DatabaseEngine.MySql);
        job.ScopeInput = DiscoveryScopeSelection.LocalNetworks(["192.168.10.0/24"]);
        job.Results.Single().Host = "192.168.10.25";
        var allowlist = new StubAllowlistService();
        var service = CreateService(new InMemoryDiscoveryRepository(job), allowlistService: allowlist);

        var response = await service.ApproveAllowlistAsync(job.Id, [job.Results.Single().Id]);

        Assert.Equal(1, response.ApprovedCount);
        var command = Assert.Single(allowlist.Commands);
        Assert.Equal("IPv4", command.TargetType);
        Assert.Equal("192.168.10.25", command.TargetValue);
        Assert.Equal([3306], command.AllowedPorts!["mysql"]);
    }

    [Fact]
    public async Task AddTargetsAsync_AddsSupportedDatabaseAsNormalTarget()
    {
        var job = CreateCompletedJob(DiscoveryResultStatus.Supported, DatabaseEngine.PostgreSql);
        var repository = new InMemoryDiscoveryRepository(job);
        var targetRepository = new InMemoryTargetRepository();
        var targetService = new StubTargetService(targetRepository);
        var internalScope = new StubAllowlistService();
        var service = CreateService(repository, targetRepository, targetService, allowlistService: internalScope);

        var added = await service.AddTargetsAsync(job.Id, [job.Results.Single().Id], isBulk: false);

        Assert.Equal(1, added.AddedCount);
        Assert.Single(targetRepository.Items);
        Assert.Equal("postgresql", targetRepository.Items[0].Engine.ToApiString());
        Assert.Equal(DiscoveryResultStatus.AddedAsTarget, job.Results.Single().Status);
        var scopeCommand = Assert.Single(internalScope.Commands);
        Assert.Equal(job.EnvironmentTag, scopeCommand.Environment);
        Assert.Equal([5432], scopeCommand.AllowedPorts!["postgresql"]);
    }

    [Fact]
    public async Task AddTargetsAsync_AddsOracleAsNormalTarget()
    {
        var job = CreateCompletedJob(DiscoveryResultStatus.Supported, DatabaseEngine.Oracle);
        var repository = new InMemoryDiscoveryRepository(job);
        var targetRepository = new InMemoryTargetRepository();
        var service = CreateService(repository, targetRepository, new StubTargetService(targetRepository));

        var added = await service.AddTargetsAsync(job.Id, [job.Results.Single().Id], isBulk: false);

        var target = Assert.Single(targetRepository.Items);
        Assert.Equal(DatabaseEngine.Oracle, target.Engine);
        Assert.Equal(1521, target.Port);
        Assert.Equal("oracle", Assert.Single(added.Results).DetectedEngine);
    }

    [Fact]
    public async Task AddTargetsAsync_RejectsUnsupportedDatabase()
    {
        var job = CreateCompletedJob(DiscoveryResultStatus.Unsupported, DatabaseEngine.MySql);
        var unsupportedResult = job.Results.Single();
        unsupportedResult.DetectedEngine = null;
        unsupportedResult.DetectedService = "MongoDB";
        unsupportedResult.Port = 27017;
        var targetRepository = new InMemoryTargetRepository();
        var targetService = new StubTargetService(targetRepository);
        var service = CreateService(new InMemoryDiscoveryRepository(job), targetRepository, targetService);

        var exception = await Assert.ThrowsAsync<DbScanner.Application.Exceptions.DiscoveryTargetNotSupportedException>(() =>
            service.AddTargetsAsync(job.Id, [job.Results.Single().Id], isBulk: false));

        Assert.Equal("TARGET_NOT_SUPPORTED", DbScanner.Application.Exceptions.DiscoveryTargetNotSupportedException.ErrorCode);
        Assert.Empty(targetRepository.Items);
        Assert.Equal(0, targetService.CreateCount);
    }

    [Fact]
    public async Task AddTargetsAsync_RejectsUnconfirmedPortHintEvenWhenLegacyEngineIsPresent()
    {
        var job = CreateCompletedJob(DiscoveryResultStatus.Supported, DatabaseEngine.MySql);
        job.Results.Single().FingerprintLevel = DiscoveryFingerprintLevel.PortHint;
        var targetRepository = new InMemoryTargetRepository();
        var targetService = new StubTargetService(targetRepository);
        var service = CreateService(new InMemoryDiscoveryRepository(job), targetRepository, targetService);

        await Assert.ThrowsAsync<DiscoveryTargetNotSupportedException>(() =>
            service.AddTargetsAsync(job.Id, [job.Results.Single().Id], isBulk: false));

        Assert.Empty(targetRepository.Items);
        Assert.Equal(0, targetService.CreateCount);
    }

    [Fact]
    public async Task AddTargetsAsync_ReturnsConflictForExistingTarget()
    {
        var job = CreateCompletedJob(DiscoveryResultStatus.Supported, DatabaseEngine.MySql);
        var existing = new Target
        {
            Name = "Existing MySQL",
            Host = "demo-database",
            Port = 3306,
            Engine = DatabaseEngine.MySql,
            EnvironmentTag = "demo"
        };
        var targetRepository = new InMemoryTargetRepository(existing);
        var targetService = new StubTargetService(targetRepository);
        var service = CreateService(new InMemoryDiscoveryRepository(job), targetRepository, targetService);

        var exception = await Assert.ThrowsAsync<DbScanner.Application.Exceptions.TargetAlreadyExistsException>(() =>
            service.AddTargetsAsync(job.Id, [job.Results.Single().Id], isBulk: false));

        Assert.Single(targetRepository.Items);
        Assert.Equal(0, targetService.CreateCount);
        Assert.Equal(existing.Id, exception.ExistingTargetId);
    }

    [Fact]
    public async Task GetAsync_MarksMatchingInventoryTargetAsAlreadyAdded()
    {
        var job = CreateCompletedJob(DiscoveryResultStatus.Supported, DatabaseEngine.MySql);
        var existing = new Target
        {
            Name = "Existing MySQL",
            Host = "DEMO-DATABASE.",
            Port = 3306,
            Engine = DatabaseEngine.MySql,
            EnvironmentTag = "other-environment"
        };
        var service = CreateService(
            new InMemoryDiscoveryRepository(job),
            new InMemoryTargetRepository(existing));

        var response = await service.GetAsync(job.Id);
        var result = Assert.Single(response!.SupportedDatabases);

        Assert.True(result.IsAlreadyTarget);
        Assert.Equal(existing.Id, result.ExistingTargetId);
        Assert.Equal("AlreadyAdded", result.TargetState);
        Assert.False(result.CanAddAsTarget);
    }

    [Fact]
    public async Task GetAsync_DoesNotExposeStaleTargetIdWhenTargetNoLongerExists()
    {
        var job = CreateCompletedJob(DiscoveryResultStatus.AddedAsTarget, DatabaseEngine.MySql);
        job.Results.Single().TargetId = Guid.NewGuid();
        var service = CreateService(
            new InMemoryDiscoveryRepository(job),
            new InMemoryTargetRepository());

        var response = await service.GetAsync(job.Id);
        var result = Assert.Single(response!.SupportedDatabases);

        Assert.False(result.IsAlreadyTarget);
        Assert.Null(result.ExistingTargetId);
        Assert.Null(result.TargetId);
        Assert.Equal("Available", result.TargetState);
        Assert.True(result.CanAddAsTarget);
    }

    [Fact]
    public async Task GetAsync_DoesNotPromoteUnsupportedAlreadyDiscoveredResult()
    {
        var job = new DiscoveryJob
        {
            Name = "Repeated Redis discovery",
            ScopeInput = "redis.internal",
            EnvironmentTag = "demo",
            SupportedEngineFilter = "mysql,postgresql,mssql,oracle",
            Status = DiscoveryStatus.Completed
        };
        job.Results.Add(new DiscoveredDatabase
        {
            DiscoveryJobId = job.Id,
            Host = "redis.internal",
            Port = 6379,
            DetectedService = "Redis",
            DetectedEngine = null,
            Confidence = 65,
            Status = DiscoveryResultStatus.AlreadyDiscovered,
            Reason = "Redis is not supported yet."
        });
        var service = CreateService(
            new InMemoryDiscoveryRepository(job),
            new InMemoryTargetRepository());

        var response = await service.GetAsync(job.Id);
        var result = Assert.Single(response!.UnsupportedDatabases);

        Assert.Empty(response.SupportedDatabases);
        Assert.Equal("unsupported", result.Status);
        Assert.Equal("Unsupported", result.TargetState);
        Assert.False(result.CanAddAsTarget);
    }

    [Fact]
    public async Task DeleteAsync_RemovesCompletedJobAndItsResults()
    {
        var job = CreateCompletedJob(DiscoveryResultStatus.Supported, DatabaseEngine.MySql);
        var repository = new InMemoryDiscoveryRepository(job);
        var service = CreateService(repository);

        var deleted = await service.DeleteAsync(job.Id);

        Assert.True(deleted);
        Assert.Null(await repository.GetByIdAsync(job.Id));
        Assert.Null(await repository.GetResultAsync(job.Id, job.Results.Single().Id));
    }

    [Fact]
    public async Task DeleteAsync_RejectsActiveJob()
    {
        var job = CreateCompletedJob(DiscoveryResultStatus.Supported, DatabaseEngine.MySql);
        job.Status = DiscoveryStatus.Running;
        var repository = new InMemoryDiscoveryRepository(job);
        var service = CreateService(repository);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(job.Id));

        Assert.NotNull(await repository.GetByIdAsync(job.Id));
    }

    [Fact]
    public async Task AddTargetsAsync_BulkReportsAddedExistingAndFailedSeparately()
    {
        var job = CreateCompletedJob(DiscoveryResultStatus.Supported, DatabaseEngine.MySql);
        job.Results.Add(new DiscoveredDatabase
        {
            DiscoveryJobId = job.Id,
            Host = "new-postgres",
            Port = 5432,
            DetectedService = "PostgreSQL",
            DetectedEngine = DatabaseEngine.PostgreSql,
            Confidence = 90,
            FingerprintLevel = DiscoveryFingerprintLevel.Confirmed,
            Status = DiscoveryResultStatus.Supported,
            Reason = "Supported"
        });
        job.Results.Add(new DiscoveredDatabase
        {
            DiscoveryJobId = job.Id,
            Host = "unsupported-redis",
            Port = 6379,
            DetectedService = "Redis",
            DetectedEngine = null,
            Confidence = 30,
            FingerprintLevel = DiscoveryFingerprintLevel.PortHint,
            Status = DiscoveryResultStatus.Unsupported,
            Reason = "Not supported"
        });
        var existing = new Target
        {
            Name = "Existing MySQL",
            Host = "demo-database",
            Port = 3306,
            Engine = DatabaseEngine.MySql,
            EnvironmentTag = "demo"
        };
        var targets = new InMemoryTargetRepository(existing);
        var service = CreateService(
            new InMemoryDiscoveryRepository(job),
            targets,
            new StubTargetService(targets));

        var response = await service.AddTargetsAsync(
            job.Id,
            job.Results.Select(result => result.Id).ToArray(),
            isBulk: true);

        Assert.Equal(1, response.AddedCount);
        Assert.Equal(1, response.AlreadyExistingCount);
        Assert.Equal(1, response.FailedCount);
        Assert.Equal(2, targets.Items.Count);
        Assert.Contains(response.Items, item => item.Status == "alreadyExists" && item.Code == "TARGET_ALREADY_EXISTS");
        Assert.Contains(response.Items, item => item.Status == "failed" && item.Code == "TARGET_NOT_SUPPORTED");
    }

    private static DiscoveryService CreateService(
        InMemoryDiscoveryRepository? repository = null,
        InMemoryTargetRepository? targetRepository = null,
        StubTargetService? targetService = null,
        StubScopeValidator? scopeValidator = null,
        StubDiscoveryPublisher? publisher = null,
        StubAllowlistService? allowlistService = null)
    {
        repository ??= new InMemoryDiscoveryRepository();
        targetRepository ??= new InMemoryTargetRepository();
        targetService ??= new StubTargetService(targetRepository);
        return new DiscoveryService(
            repository,
            publisher ?? new StubDiscoveryPublisher(),
            scopeValidator ?? new StubScopeValidator(authorized: true),
            new StubLocalNetworkProvider(),
            allowlistService ?? new StubAllowlistService(),
            targetRepository,
            targetService);
    }

    private static DiscoveryJob CreateCompletedJob(DiscoveryResultStatus status, DatabaseEngine engine)
    {
        var port = engine switch
        {
            DatabaseEngine.MySql => 3306,
            DatabaseEngine.PostgreSql => 5432,
            DatabaseEngine.MsSql => 1433,
            _ => 1521
        };
        var job = new DiscoveryJob
        {
            Name = "Demo discovery",
            ScopeInput = "demo-database",
            EnvironmentTag = "demo",
            SupportedEngineFilter = "mysql,postgresql,mssql",
            Status = DiscoveryStatus.Completed
        };
        job.Results.Add(new DiscoveredDatabase
        {
            DiscoveryJobId = job.Id,
            Host = "demo-database",
            Port = port,
            DetectedService = engine.ToDisplayName(),
            DetectedEngine = engine,
            Confidence = 90,
            FingerprintLevel = DiscoveryFingerprintLevel.Confirmed,
            Status = status,
            Reason = status == DiscoveryResultStatus.Supported ? "Supported" : "Not supported"
        });
        return job;
    }

    private sealed class InMemoryDiscoveryRepository(params DiscoveryJob[] jobs) : IDiscoveryRepository
    {
        private readonly List<DiscoveryJob> _jobs = jobs.ToList();

        public Task<IReadOnlyList<DiscoveryJob>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DiscoveryJob>>(_jobs);
        public Task<DiscoveryJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_jobs.FirstOrDefault(job => job.Id == id));
        public Task<DiscoveredDatabase?> GetResultAsync(Guid jobId, Guid resultId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_jobs.FirstOrDefault(job => job.Id == jobId)?.Results.FirstOrDefault(result => result.Id == resultId));
        public Task AddAsync(DiscoveryJob job, CancellationToken cancellationToken = default)
        {
            _jobs.Add(job);
            return Task.CompletedTask;
        }
        public Task DeleteAsync(DiscoveryJob job, CancellationToken cancellationToken = default)
        {
            _jobs.Remove(job);
            return Task.CompletedTask;
        }
        public Task<bool> TryMarkRunningAsync(Guid jobId, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task CompleteAsync(Guid jobId, IReadOnlyCollection<DiscoveredDatabase> results, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task FailAsync(Guid jobId, string errorMessage, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubDiscoveryPublisher : IDiscoveryJobPublisher
    {
        public int PublishCount { get; private set; }
        public Task PublishRequestedAsync(DiscoveryJob job, CancellationToken cancellationToken = default)
        {
            PublishCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubScopeValidator(
        bool authorized,
        IReadOnlyList<DiscoveryTargetValidationDto>? detailedTargets = null) : IDiscoveryScopeValidator
    {
        public int PlanAuthorizedCount { get; private set; }
        public int PlanLocalCount { get; private set; }
        public int ValidateCustomCount { get; private set; }

        public Task<IReadOnlyList<DiscoveryEndpoint>> PlanAuthorizedAsync(
            string environment,
            IReadOnlySet<DatabaseEngine> supportedEngineFilter,
            CancellationToken cancellationToken = default)
        {
            PlanAuthorizedCount++;
            return authorized
                ? Task.FromResult<IReadOnlyList<DiscoveryEndpoint>>([new DiscoveryEndpoint("authorized-database", 3306)])
                : throw new InvalidOperationException("No enabled authorized discovery scopes are configured.");
        }

        public Task<IReadOnlyList<DiscoveryEndpoint>> PlanLocalNetworksAsync(
            IReadOnlyCollection<string> cidrs,
            IReadOnlySet<DatabaseEngine> supportedEngineFilter,
            CancellationToken cancellationToken = default)
        {
            PlanLocalCount++;
            return authorized
                ? Task.FromResult<IReadOnlyList<DiscoveryEndpoint>>([new DiscoveryEndpoint("192.168.10.25", 3306, DatabaseEngine.MySql)])
                : throw new InvalidOperationException("Local network is not eligible.");
        }

        public Task<IReadOnlyList<DiscoveryEndpoint>> ValidateAndPlanAsync(
            string scopeInput,
            string environment,
            IReadOnlySet<DatabaseEngine> supportedEngineFilter,
            CancellationToken cancellationToken = default)
        {
            ValidateCustomCount++;
            return authorized
                ? Task.FromResult<IReadOnlyList<DiscoveryEndpoint>>([new DiscoveryEndpoint(scopeInput, 3306)])
                : throw new InvalidOperationException("Discovery scope is outside the approved allowlist.");
        }

        public Task<DiscoveryScopePlan> ValidateAndPlanDetailedAsync(
            string scopeInput,
            string environment,
            IReadOnlySet<DatabaseEngine> supportedEngineFilter,
            CancellationToken cancellationToken = default)
        {
            ValidateCustomCount++;
            var targets = detailedTargets ??
                [new DiscoveryTargetValidationDto(
                    scopeInput,
                    authorized ? DiscoveryTargetValidationStatus.Authorized : DiscoveryTargetValidationStatus.NotInAllowlist,
                    authorized ? "Authorized" : "Not authorized",
                    [])];
            var endpoints = targets
                .Where(target => target.Status == DiscoveryTargetValidationStatus.Authorized)
                .Select(target => new DiscoveryEndpoint(target.Target, 3306))
                .ToArray();
            return Task.FromResult(new DiscoveryScopePlan(endpoints, targets));
        }
    }

    private sealed class StubLocalNetworkProvider : ILocalNetworkProvider
    {
        public IReadOnlyList<LocalNetworkRange> GetPrivateIpv4Networks() =>
            [new("Ethernet", "192.168.10.5", "192.168.10.0/24", 254, true)];
    }

    private sealed class StubAllowlistService : IDiscoveryAllowlistService
    {
        public List<UpsertDiscoveryAllowlistCommand> Commands { get; } = [];

        public Task<DiscoveryAllowlistEntryDto> CreateOrMergeAsync(
            UpsertDiscoveryAllowlistCommand command,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.FromResult(new DiscoveryAllowlistEntryDto(
                Guid.NewGuid(), command.Name, command.Environment, command.TargetType, command.TargetValue,
                command.AllowedEngines.ToArray(), command.AllowedPorts ?? new Dictionary<string, IReadOnlyList<int>>(),
                command.IsActive, command.Description, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }

        public Task<IReadOnlyList<DiscoveryAllowlistEntryDto>> ListAsync(string? environment = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DiscoveryAllowlistEntryDto>>([]);
        public Task<DiscoveryAllowlistEntryDto> CreateAsync(UpsertDiscoveryAllowlistCommand command, CancellationToken cancellationToken = default) =>
            CreateOrMergeAsync(command, cancellationToken);
        public Task<DiscoveryAllowlistEntryDto> UpdateAsync(Guid id, UpsertDiscoveryAllowlistCommand command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<DiscoveryAllowlistEntryDto> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class InMemoryTargetRepository(params Target[] targets) : ITargetRepository
    {
        public List<Target> Items { get; } = targets.ToList();
        public Task<IReadOnlyList<Target>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Target>>(Items);
        public Task<Target?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));
        public Task<Target?> FindByEndpointAsync(
            string normalizedHost,
            int port,
            DatabaseEngine engine,
            Guid? excludeTargetId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.FirstOrDefault(item =>
                item.Id != excludeTargetId
                && TargetEndpointNormalizer.NormalizeHost(item.Host) == normalizedHost
                && item.Port == port
                && item.Engine == engine));
        public Task AddAsync(Target target, CancellationToken cancellationToken = default) { Items.Add(target); return Task.CompletedTask; }
        public Task DeleteAsync(Target target, CancellationToken cancellationToken = default) { Items.Remove(target); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubTargetService(InMemoryTargetRepository repository) : ITargetService
    {
        public int CreateCount { get; private set; }
        public Task<IReadOnlyList<TargetDto>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TargetDto> CreateAsync(CreateTargetCommand command, CancellationToken cancellationToken = default)
        {
            CreateCount++;
            var target = new Target
            {
                Name = command.Name,
                Host = command.Host!,
                NormalizedHost = TargetEndpointNormalizer.NormalizeHost(command.Host!),
                Port = command.Port!.Value,
                Engine = command.Engine,
                EnvironmentTag = command.EnvironmentTag
            };
            repository.Items.Add(target);
            return Task.FromResult(new TargetDto(
                target.Id, target.Name, target.Host, target.Port, target.Engine.ToApiString(),
                "defaultValues", target.EnvironmentTag, false, true, null));
        }
        public Task<TargetDto> UpdateAsync(Guid targetId, UpdateTargetCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid targetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
