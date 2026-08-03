using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Application.Services;
using DbScanner.Contracts.Targets;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
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
                [DatabaseEngine.MySql])));

        Assert.Equal(0, publisher.PublishCount);
    }

    [Fact]
    public async Task AddTargetsAsync_AddsSupportedDatabaseAsNormalTarget()
    {
        var job = CreateCompletedJob(DiscoveryResultStatus.Supported, DatabaseEngine.PostgreSql);
        var repository = new InMemoryDiscoveryRepository(job);
        var targetRepository = new InMemoryTargetRepository();
        var targetService = new StubTargetService(targetRepository);
        var service = CreateService(repository, targetRepository, targetService);

        var added = await service.AddTargetsAsync(job.Id, [job.Results.Single().Id]);

        Assert.Single(added);
        Assert.Single(targetRepository.Items);
        Assert.Equal("postgresql", targetRepository.Items[0].Engine.ToApiString());
        Assert.Equal(DiscoveryResultStatus.AddedAsTarget, job.Results.Single().Status);
    }

    [Fact]
    public async Task AddTargetsAsync_AddsOracleAsNormalTarget()
    {
        var job = CreateCompletedJob(DiscoveryResultStatus.Supported, DatabaseEngine.Oracle);
        var repository = new InMemoryDiscoveryRepository(job);
        var targetRepository = new InMemoryTargetRepository();
        var service = CreateService(repository, targetRepository, new StubTargetService(targetRepository));

        var added = await service.AddTargetsAsync(job.Id, [job.Results.Single().Id]);

        var target = Assert.Single(targetRepository.Items);
        Assert.Equal(DatabaseEngine.Oracle, target.Engine);
        Assert.Equal(1521, target.Port);
        Assert.Equal("oracle", Assert.Single(added).DetectedEngine);
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

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.AddTargetsAsync(job.Id, [job.Results.Single().Id]));

        Assert.Equal("Unsupported discovery results cannot be added as targets.", exception.Message);
        Assert.Empty(targetRepository.Items);
        Assert.Equal(0, targetService.CreateCount);
    }

    [Fact]
    public async Task AddTargetsAsync_ReusesExistingTargetWithoutDuplicate()
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

        var added = await service.AddTargetsAsync(job.Id, [job.Results.Single().Id]);

        Assert.Single(targetRepository.Items);
        Assert.Equal(0, targetService.CreateCount);
        Assert.Equal(existing.Id, added.Single().TargetId);
    }

    private static DiscoveryService CreateService(
        InMemoryDiscoveryRepository? repository = null,
        InMemoryTargetRepository? targetRepository = null,
        StubTargetService? targetService = null,
        StubScopeValidator? scopeValidator = null,
        StubDiscoveryPublisher? publisher = null)
    {
        repository ??= new InMemoryDiscoveryRepository();
        targetRepository ??= new InMemoryTargetRepository();
        targetService ??= new StubTargetService(targetRepository);
        return new DiscoveryService(
            repository,
            publisher ?? new StubDiscoveryPublisher(),
            scopeValidator ?? new StubScopeValidator(authorized: true),
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

    private sealed class StubScopeValidator(bool authorized) : IDiscoveryScopeValidator
    {
        public Task<IReadOnlyList<DiscoveryEndpoint>> ValidateAndPlanAsync(
            string scopeInput,
            IReadOnlySet<DatabaseEngine> supportedEngineFilter,
            CancellationToken cancellationToken = default) =>
            authorized
                ? Task.FromResult<IReadOnlyList<DiscoveryEndpoint>>([new DiscoveryEndpoint(scopeInput, 3306)])
                : throw new InvalidOperationException("Discovery scope is outside the approved allowlist.");
    }

    private sealed class InMemoryTargetRepository(params Target[] targets) : ITargetRepository
    {
        public List<Target> Items { get; } = targets.ToList();
        public Task<IReadOnlyList<Target>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Target>>(Items);
        public Task<Target?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));
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
