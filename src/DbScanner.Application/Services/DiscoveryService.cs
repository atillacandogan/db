using System.Net;
using System.Net.Sockets;
using DbScanner.Application.Exceptions;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Contracts.Discovery;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Domain.Services;
using DbScanner.Domain;

namespace DbScanner.Application.Services;

public sealed class DiscoveryService(
    IDiscoveryRepository discoveryRepository,
    IDiscoveryJobPublisher discoveryJobPublisher,
    IDiscoveryScopeValidator scopeValidator,
    ILocalNetworkProvider localNetworkProvider,
    IDiscoveryAllowlistService allowlistService,
    ITargetRepository targetRepository,
    ITargetService targetService) : IDiscoveryService
{
    private static readonly DatabaseEngine[] SupportedEngines =
        [DatabaseEngine.MySql, DatabaseEngine.PostgreSql, DatabaseEngine.MsSql, DatabaseEngine.Oracle];

    public async Task<IReadOnlyList<DiscoveryJobDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await discoveryRepository.ListAsync(cancellationToken);
        var targets = await targetRepository.ListAsync(cancellationToken);
        return jobs.Select(job => MapJob(job, targets)).ToArray();
    }

    public async Task<DiscoveryJobDto?> GetAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await discoveryRepository.GetByIdAsync(jobId, cancellationToken);
        if (job is null)
        {
            return null;
        }

        var targets = await targetRepository.ListAsync(cancellationToken);
        return MapJob(job, targets);
    }

    public async Task<bool> DeleteAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await discoveryRepository.GetByIdAsync(jobId, cancellationToken);
        if (job is null)
        {
            return false;
        }

        if (job.Status is DiscoveryStatus.Pending or DiscoveryStatus.Running)
        {
            throw new InvalidOperationException("An active discovery job cannot be deleted.");
        }

        await discoveryRepository.DeleteAsync(job, cancellationToken);
        await discoveryRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<IReadOnlyList<LocalNetworkDto>> ListLocalNetworksAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<LocalNetworkDto>>(localNetworkProvider.GetPrivateIpv4Networks()
            .Select(network => new LocalNetworkDto(
                network.InterfaceName,
                network.Address,
                network.Cidr,
                network.HostCount,
                network.CanScan,
                network.Reason))
            .ToArray());
    }

    public async Task<DiscoveryJobDto> CreateLocalNetworkAsync(
        string name,
        string environment,
        IReadOnlyCollection<DatabaseEngine> supportedEngines,
        IReadOnlyCollection<string>? networks,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeName(name);
        var normalizedEnvironment = NormalizeEnvironment(environment);
        var engineFilter = NormalizeEngineFilter(supportedEngines);
        var detected = localNetworkProvider.GetPrivateIpv4Networks();
        var requested = networks is { Count: > 0 }
            ? networks.Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            : detected.Where(network => network.CanScan).Select(network => network.Cidr).ToArray();
        if (requested.Length == 0)
        {
            throw new InvalidOperationException(
                "No scannable private IPv4 network was detected. Docker, VPN, virtual and oversized networks are excluded.");
        }

        foreach (var cidr in requested)
        {
            var match = detected.FirstOrDefault(network =>
                network.Cidr.Equals(cidr, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new InvalidOperationException($"Network '{cidr}' was not detected on an eligible local interface.");
            }

            if (!match.CanScan)
            {
                throw new InvalidOperationException(match.Reason ?? $"Network '{cidr}' exceeds the discovery safety limit.");
            }
        }

        var endpoints = await scopeValidator.PlanLocalNetworksAsync(requested, engineFilter, cancellationToken);
        if (endpoints.Count == 0)
        {
            throw new InvalidOperationException("The selected local networks contain no database endpoints to probe.");
        }

        return await QueueJobAsync(
            normalizedName,
            DiscoveryScopeSelection.LocalNetworks(requested),
            normalizedEnvironment,
            engineFilter,
            cancellationToken);
    }

    public async Task<DiscoveryJobDto> CreateAsync(
        CreateDiscoveryCommand command,
        CancellationToken cancellationToken = default)
    {
        var name = NormalizeName(command.Name);
        var scope = command.Scope?.Trim() ?? string.Empty;
        var environment = NormalizeEnvironment(command.Environment);

        if (!command.UseAuthorizedScope && (string.IsNullOrWhiteSpace(scope) || scope.Length > 4096))
        {
            throw new InvalidOperationException("Discovery scope is required and must not exceed 4096 characters.");
        }

        var engineFilter = NormalizeEngineFilter(command.SupportedEngineFilter);
        IReadOnlyList<DiscoveryEndpoint> plannedEndpoints;
        var queuedScope = DiscoveryScopeSelection.AllAuthorized;
        if (command.UseAuthorizedScope)
        {
            plannedEndpoints = await scopeValidator.PlanAuthorizedAsync(
                environment,
                engineFilter,
                cancellationToken);
        }
        else
        {
            var plan = await scopeValidator.ValidateAndPlanDetailedAsync(
                scope,
                environment,
                engineFilter,
                cancellationToken);
            plannedEndpoints = plan.Endpoints;
            queuedScope = string.Join(
                '\n',
                plan.Targets
                    .Where(target => target.Status == DiscoveryTargetValidationStatus.Authorized)
                    .Select(target => target.Target));
        }

        if (plannedEndpoints.Count == 0)
        {
            throw new InvalidOperationException("The selected scope contains no permitted database endpoints.");
        }

        return await QueueJobAsync(
            name,
            queuedScope,
            environment,
            engineFilter,
            cancellationToken);
    }

    public async Task<DiscoveryScopeValidationDto> ValidateScopeAsync(
        string scope,
        string environment,
        IReadOnlyCollection<DatabaseEngine> supportedEngines,
        CancellationToken cancellationToken = default)
    {
        if (!DiscoveryEnvironments.TryNormalize(environment, out var normalizedEnvironment))
        {
            throw new InvalidOperationException(
                $"Unsupported environment. Allowed values: {string.Join(", ", DiscoveryEnvironments.All.Order())}.");
        }

        var plan = await scopeValidator.ValidateAndPlanDetailedAsync(
            scope,
            normalizedEnvironment,
            NormalizeEngineFilter(supportedEngines),
            cancellationToken);
        return new DiscoveryScopeValidationDto(normalizedEnvironment, plan.IsValid, plan.Targets);
    }

    public async Task<AddDiscoveryTargetsResponseDto> AddTargetsAsync(
        Guid jobId,
        IReadOnlyCollection<Guid> resultIds,
        bool isBulk,
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

        if (!isBulk && selected.Any(IsUnsupported))
        {
            throw new DiscoveryTargetNotSupportedException();
        }

        var targets = (await targetRepository.ListAsync(cancellationToken)).ToList();
        if (!isBulk)
        {
            var duplicate = selected
                .Select(result => FindExistingTarget(result, targets))
                .FirstOrDefault(target => target is not null);
            if (duplicate is not null)
            {
                throw new TargetAlreadyExistsException(duplicate.Id);
            }
        }

        var items = new List<DiscoveryTargetAddItemDto>(selected.Length);
        foreach (var result in selected)
        {
            if (IsUnsupported(result))
            {
                items.Add(new DiscoveryTargetAddItemDto(
                    result.Id,
                    "failed",
                    DiscoveryTargetNotSupportedException.ErrorCode,
                    DiscoveryTargetNotSupportedException.UserMessage,
                    null));
                continue;
            }

            var existing = FindExistingTarget(result, targets);

            if (existing is not null)
            {
                MarkAdded(result, existing.Id);
                items.Add(new DiscoveryTargetAddItemDto(
                    result.Id,
                    "alreadyExists",
                    TargetAlreadyExistsException.ErrorCode,
                    TargetAlreadyExistsException.UserMessage,
                    existing.Id));
                continue;
            }

            try
            {
                await EnsureDiscoveredTargetScopeAsync(job, result, cancellationToken);
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
                targets.Add(new Target
                {
                    Id = target.Id,
                    Name = target.Name,
                    Host = target.Host,
                    NormalizedHost = TargetEndpointNormalizer.NormalizeHost(target.Host),
                    Port = target.Port,
                    Engine = result.DetectedEngine.Value,
                    EnvironmentTag = target.EnvironmentTag
                });
                items.Add(new DiscoveryTargetAddItemDto(result.Id, "added", null, null, target.Id));
            }
            catch (TargetAlreadyExistsException exception)
            {
                if (!isBulk)
                {
                    throw;
                }

                if (exception.ExistingTargetId.HasValue)
                {
                    MarkAdded(result, exception.ExistingTargetId.Value);
                }

                items.Add(new DiscoveryTargetAddItemDto(
                    result.Id,
                    "alreadyExists",
                    TargetAlreadyExistsException.ErrorCode,
                    TargetAlreadyExistsException.UserMessage,
                    exception.ExistingTargetId));
            }
            catch (InvalidOperationException exception)
            {
                if (!isBulk)
                {
                    throw;
                }

                items.Add(new DiscoveryTargetAddItemDto(
                    result.Id,
                    "failed",
                    "TARGET_ADD_FAILED",
                    exception.Message,
                    null));
            }
        }

        await discoveryRepository.SaveChangesAsync(cancellationToken);
        var refreshedTargets = await targetRepository.ListAsync(cancellationToken);
        return new AddDiscoveryTargetsResponseDto(
            items.Count(item => item.Status == "added"),
            items.Count(item => item.Status == "alreadyExists"),
            items.Count(item => item.Status == "failed"),
            items,
            selected.Select(result => MapResult(job, result, refreshedTargets)).ToArray());
    }

    private async Task EnsureDiscoveredTargetScopeAsync(
        DiscoveryJob job,
        DiscoveredDatabase result,
        CancellationToken cancellationToken)
    {
        var engine = result.DetectedEngine
            ?? throw new DiscoveryTargetNotSupportedException();
        var targetType = IPAddress.TryParse(result.Host, out var address)
            && address.AddressFamily == AddressFamily.InterNetwork
                ? "IPv4"
                : "Host";
        var engineName = engine.ToApiString();

        await allowlistService.CreateOrMergeAsync(
            new UpsertDiscoveryAllowlistCommand(
                $"Discovery target {result.Host}",
                job.EnvironmentTag,
                targetType,
                result.Host,
                [engineName],
                new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase)
                {
                    [engineName] = [result.Port]
                },
                IsActive: true,
                $"Internal scan scope created from discovery job '{job.Name}'."),
            cancellationToken);
    }

    public async Task<ApproveDiscoveryAllowlistResponseDto> ApproveAllowlistAsync(
        Guid jobId,
        IReadOnlyCollection<Guid> resultIds,
        CancellationToken cancellationToken = default)
    {
        var job = await discoveryRepository.GetByIdAsync(jobId, cancellationToken)
            ?? throw new InvalidOperationException("Discovery job was not found.");
        if (!DiscoveryScopeSelection.TryGetLocalNetworks(job.ScopeInput, out _))
        {
            throw new InvalidOperationException("Only local network discovery candidates can be approved into the allowlist.");
        }

        var selectedIds = resultIds.Distinct().ToArray();
        if (selectedIds.Length == 0)
        {
            throw new InvalidOperationException("At least one discovered database must be selected.");
        }

        var selected = job.Results.Where(result => selectedIds.Contains(result.Id)).ToArray();
        if (selected.Length != selectedIds.Length)
        {
            throw new InvalidOperationException("One or more discovery results were not found.");
        }

        if (selected.Any(result => result.DetectedEngine is null
            || result.FingerprintLevel != DiscoveryFingerprintLevel.Confirmed
            || result.Status == DiscoveryResultStatus.Unsupported
            || !IPAddress.TryParse(result.Host, out var address)
            || !IsPrivateIpv4(address)))
        {
            throw new InvalidOperationException("Only supported databases on private IPv4 addresses can be approved.");
        }

        var entries = new List<DiscoveryAllowlistEntryDto>();
        foreach (var group in selected.GroupBy(result => result.Host, StringComparer.OrdinalIgnoreCase))
        {
            var engines = group.Select(result => result.DetectedEngine!.Value).Distinct().ToArray();
            var ports = engines.ToDictionary(
                engine => engine.ToApiString(),
                engine => (IReadOnlyList<int>)group
                    .Where(result => result.DetectedEngine == engine)
                    .Select(result => result.Port)
                    .Distinct()
                    .Order()
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
            entries.Add(await allowlistService.CreateOrMergeAsync(
                new UpsertDiscoveryAllowlistCommand(
                    $"Discovered database {group.Key}",
                    job.EnvironmentTag,
                    "IPv4",
                    group.Key,
                    engines.Select(engine => engine.ToApiString()).ToArray(),
                    ports,
                    IsActive: true,
                    $"Approved from local network discovery job '{job.Name}'."),
                cancellationToken));
        }

        return new ApproveDiscoveryAllowlistResponseDto(entries.Count, entries);
    }

    private async Task<DiscoveryJobDto> QueueJobAsync(
        string name,
        string scopeInput,
        string environment,
        IReadOnlySet<DatabaseEngine> engineFilter,
        CancellationToken cancellationToken)
    {
        var job = new DiscoveryJob
        {
            Name = name,
            ScopeInput = scopeInput,
            EnvironmentTag = environment,
            SupportedEngineFilter = string.Join(',', engineFilter.Select(engine => engine.ToApiString())),
            Status = DiscoveryStatus.Pending
        };

        await discoveryRepository.AddAsync(job, cancellationToken);
        await discoveryRepository.SaveChangesAsync(cancellationToken);
        await discoveryJobPublisher.PublishRequestedAsync(job, cancellationToken);
        return MapJob(job, []);
    }

    private static string NormalizeName(string value)
    {
        var name = value.Trim();
        return !string.IsNullOrWhiteSpace(name) && name.Length <= 160
            ? name
            : throw new InvalidOperationException("Discovery name is required and must not exceed 160 characters.");
    }

    private static string NormalizeEnvironment(string value) =>
        DiscoveryEnvironments.TryNormalize(value, out var environment)
            ? environment
            : throw new InvalidOperationException(
                $"Unsupported environment. Allowed values: {string.Join(", ", DiscoveryEnvironments.All.Order())}.");

    private static bool IsPrivateIpv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || bytes[0] == 192 && bytes[1] == 168
            || bytes[0] == 172 && bytes[1] is >= 16 and <= 31;
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

    private static bool IsUnsupported(DiscoveredDatabase result) =>
        result.Status == DiscoveryResultStatus.Unsupported
        || result.DetectedEngine is null
        || result.FingerprintLevel != DiscoveryFingerprintLevel.Confirmed;

    private static Target? FindExistingTarget(DiscoveredDatabase result, IEnumerable<Target> targets)
    {
        if (result.DetectedEngine is null)
        {
            return null;
        }

        var normalizedHost = TargetEndpointNormalizer.NormalizeHost(result.Host);
        return targets.FirstOrDefault(target =>
            TargetEndpointNormalizer.NormalizeHost(target.Host) == normalizedHost
            && target.Port == result.Port
            && target.Engine == result.DetectedEngine.Value);
    }

    private static string BuildTargetName(DiscoveredDatabase result)
    {
        var name = $"{result.DetectedService} {result.Host}:{result.Port}";
        return name.Length <= 160 ? name : name[..160];
    }

    private static DiscoveryJobDto MapJob(DiscoveryJob job, IReadOnlyList<Target> targets)
    {
        var results = job.Results
            .OrderBy(result => result.Host)
            .ThenBy(result => result.Port)
            .Select(result => MapResult(job, result, targets))
            .ToArray();

        var scopeDisplay = DiscoveryScopeSelection.TryGetLocalNetworks(job.ScopeInput, out var localNetworks)
            ? $"Local network: {string.Join(", ", localNetworks)}"
            : DiscoveryScopeSelection.IsAllAuthorized(job.ScopeInput)
                ? "All authorized endpoints"
                : job.ScopeInput;
        return new DiscoveryJobDto(
            job.Id,
            job.Name,
            scopeDisplay,
            job.EnvironmentTag,
            ParseEngineFilter(job.SupportedEngineFilter),
            job.Status.ToString().ToLowerInvariant(),
            job.ErrorMessage,
            job.CreatedAt,
            job.StartedAt,
            job.FinishedAt,
            results.Where(result => result.Status is "supported" or "alreadyDiscovered" or "addedAsTarget").ToArray(),
            results.Where(result => result.Status == "unsupported").ToArray());
    }

    private static DiscoveryResultDto MapResult(
        DiscoveryJob job,
        DiscoveredDatabase result,
        IReadOnlyList<Target> targets)
    {
        var existingTarget = FindExistingTarget(result, targets);
        var existingTargetId = existingTarget?.Id;
        var isAlreadyTarget = existingTarget is not null;
        var isSupported = result.DetectedEngine is not null
            && result.FingerprintLevel == DiscoveryFingerprintLevel.Confirmed
            && result.Status != DiscoveryResultStatus.Unsupported;
        var status = !isSupported
            ? "unsupported"
            : isAlreadyTarget
            ? "addedAsTarget"
            : result.Status == DiscoveryResultStatus.AlreadyDiscovered
                ? "alreadyDiscovered"
                : "supported";

        return new DiscoveryResultDto(
            result.Id,
            result.Host,
            result.Port,
            result.DetectedService,
            result.DetectedEngine?.ToApiString(),
            result.Confidence,
            result.FingerprintLevel.ToString(),
            job.EnvironmentTag,
            status,
            result.Reason,
            isSupported && !isAlreadyTarget,
            existingTargetId,
            isAlreadyTarget,
            existingTargetId,
            isAlreadyTarget
                ? "AlreadyAdded"
                : status == "alreadyDiscovered"
                    ? "AlreadyDiscovered"
                    : status == "supported" ? "Available" : "Unsupported");
    }

    private static IReadOnlyList<string> ParseEngineFilter(string value) =>
        value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
