using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Contracts.Discovery;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Application.Services;

public sealed class DiscoveryService(
    IDiscoveryRepository discoveryRepository,
    IDiscoveryJobPublisher discoveryJobPublisher,
    IDiscoveryScopeValidator scopeValidator,
    ITargetRepository targetRepository,
    ITargetService targetService) : IDiscoveryService
{
    private static readonly DatabaseEngine[] SupportedEngines =
        [DatabaseEngine.MySql, DatabaseEngine.PostgreSql, DatabaseEngine.MsSql, DatabaseEngine.Oracle];

    public async Task<IReadOnlyList<DiscoveryJobDto>> ListAsync(CancellationToken cancellationToken = default) =>
        (await discoveryRepository.ListAsync(cancellationToken)).Select(MapJob).ToArray();

    public async Task<DiscoveryJobDto?> GetAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await discoveryRepository.GetByIdAsync(jobId, cancellationToken);
        return job is null ? null : MapJob(job);
    }

    public async Task<DiscoveryJobDto> CreateAsync(
        CreateDiscoveryCommand command,
        CancellationToken cancellationToken = default)
    {
        var name = command.Name.Trim();
        var scope = command.Scope.Trim();
        var environment = command.Environment.Trim();

        if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
        {
            throw new InvalidOperationException("Discovery name is required and must not exceed 160 characters.");
        }

        if (string.IsNullOrWhiteSpace(scope) || scope.Length > 4096)
        {
            throw new InvalidOperationException("Discovery scope is required and must not exceed 4096 characters.");
        }

        if (string.IsNullOrWhiteSpace(environment) || environment.Length > 64)
        {
            throw new InvalidOperationException("Discovery environment is required and must not exceed 64 characters.");
        }

        var engineFilter = NormalizeEngineFilter(command.SupportedEngineFilter);
        var plannedEndpoints = await scopeValidator.ValidateAndPlanAsync(scope, engineFilter, cancellationToken);
        if (plannedEndpoints.Count == 0)
        {
            throw new InvalidOperationException("The authorized scope contains no permitted database endpoints.");
        }

        var job = new DiscoveryJob
        {
            Name = name,
            ScopeInput = scope,
            EnvironmentTag = environment,
            SupportedEngineFilter = string.Join(',', engineFilter.Select(engine => engine.ToApiString())),
            Status = DiscoveryStatus.Pending
        };

        await discoveryRepository.AddAsync(job, cancellationToken);
        await discoveryRepository.SaveChangesAsync(cancellationToken);
        await discoveryJobPublisher.PublishRequestedAsync(job, cancellationToken);

        return MapJob(job);
    }

    public async Task<IReadOnlyList<DiscoveryResultDto>> AddTargetsAsync(
        Guid jobId,
        IReadOnlyCollection<Guid> resultIds,
        CancellationToken cancellationToken = default)
    {
        var job = await discoveryRepository.GetByIdAsync(jobId, cancellationToken)
            ?? throw new InvalidOperationException("Discovery job was not found.");
        var selectedIds = resultIds.Distinct().ToArray();
        if (selectedIds.Length == 0)
        {
            throw new InvalidOperationException("At least one discovery result must be selected.");
        }

        var selected = job.Results.Where(result => selectedIds.Contains(result.Id)).ToArray();
        if (selected.Length != selectedIds.Length)
        {
            throw new InvalidOperationException("One or more discovery results were not found.");
        }

        if (selected.Any(result => result.Status == DiscoveryResultStatus.Unsupported || result.DetectedEngine is null))
        {
            throw new InvalidOperationException("Unsupported discovery results cannot be added as targets.");
        }

        var targets = await targetRepository.ListAsync(cancellationToken);
        foreach (var result in selected)
        {
            var existing = targets.FirstOrDefault(target =>
                target.Host.Equals(result.Host, StringComparison.OrdinalIgnoreCase)
                && target.Port == result.Port
                && target.Engine == result.DetectedEngine);

            if (existing is not null)
            {
                MarkAdded(result, existing.Id);
                continue;
            }

            var target = await targetService.CreateAsync(
                new CreateTargetCommand(
                    BuildTargetName(result),
                    result.Host,
                    result.Port,
                    result.DetectedEngine!.Value,
                    job.EnvironmentTag,
                    Enabled: true),
                cancellationToken);

            MarkAdded(result, target.Id);
        }

        await discoveryRepository.SaveChangesAsync(cancellationToken);
        return selected.Select(result => MapResult(job, result)).ToArray();
    }

    private static HashSet<DatabaseEngine> NormalizeEngineFilter(IReadOnlyCollection<DatabaseEngine> engines)
    {
        var normalized = engines.Count == 0
            ? SupportedEngines.ToHashSet()
            : engines.ToHashSet();

        if (normalized.Any(engine => !SupportedEngines.Contains(engine)))
        {
            throw new InvalidOperationException("Discovery target creation supports only MySQL, PostgreSQL, MSSQL and Oracle.");
        }

        return normalized;
    }

    private static void MarkAdded(DiscoveredDatabase result, Guid targetId)
    {
        result.TargetId = targetId;
        result.Status = DiscoveryResultStatus.AddedAsTarget;
        result.Reason = "Added to the normal target inventory.";
    }

    private static string BuildTargetName(DiscoveredDatabase result)
    {
        var name = $"{result.DetectedService} {result.Host}:{result.Port}";
        return name.Length <= 160 ? name : name[..160];
    }

    private static DiscoveryJobDto MapJob(DiscoveryJob job)
    {
        var results = job.Results
            .OrderBy(result => result.Host)
            .ThenBy(result => result.Port)
            .Select(result => MapResult(job, result))
            .ToArray();

        return new DiscoveryJobDto(
            job.Id,
            job.Name,
            job.ScopeInput,
            job.EnvironmentTag,
            ParseEngineFilter(job.SupportedEngineFilter),
            job.Status.ToString().ToLowerInvariant(),
            job.ErrorMessage,
            job.CreatedAt,
            job.StartedAt,
            job.FinishedAt,
            results.Where(result => result.Status is "supported" or "addedAsTarget").ToArray(),
            results.Where(result => result.Status == "unsupported").ToArray());
    }

    private static DiscoveryResultDto MapResult(DiscoveryJob job, DiscoveredDatabase result) =>
        new(
            result.Id,
            result.Host,
            result.Port,
            result.DetectedService,
            result.DetectedEngine?.ToApiString(),
            result.Confidence,
            job.EnvironmentTag,
            result.Status switch
            {
                DiscoveryResultStatus.AddedAsTarget => "addedAsTarget",
                DiscoveryResultStatus.Supported => "supported",
                _ => "unsupported"
            },
            result.Reason,
            result.Status == DiscoveryResultStatus.Supported,
            result.TargetId);

    private static IReadOnlyList<string> ParseEngineFilter(string value) =>
        value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
