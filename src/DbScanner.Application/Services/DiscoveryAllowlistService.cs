using System.Net;
using System.Net.Sockets;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Contracts.Discovery;
using DbScanner.Domain;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Domain.Services;

namespace DbScanner.Application.Services;

public sealed class DiscoveryAllowlistService(
    IScanScopeRepository repository,
    IDiscoveryConfiguration configuration) : IDiscoveryAllowlistService
{
    public async Task<IReadOnlyList<DiscoveryAllowlistEntryDto>> ListAsync(
        string? environment = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedEnvironment = string.IsNullOrWhiteSpace(environment)
            ? null
            : NormalizeEnvironment(environment);
        return (await repository.ListAsync(normalizedEnvironment, cancellationToken))
            .Select(Map)
            .ToArray();
    }

    public async Task<DiscoveryAllowlistEntryDto> CreateAsync(
        UpsertDiscoveryAllowlistCommand command,
        CancellationToken cancellationToken = default)
    {
        var candidate = Build(command, null);
        await EnsureNotDuplicateAsync(candidate, null, cancellationToken);
        await repository.AddAsync(candidate, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Map(candidate);
    }

    public async Task<DiscoveryAllowlistEntryDto> CreateOrMergeAsync(
        UpsertDiscoveryAllowlistCommand command,
        CancellationToken cancellationToken = default)
    {
        var candidate = Build(command, null);
        var existing = (await repository.ListAsync(candidate.EnvironmentTag, cancellationToken))
            .FirstOrDefault(entry => entry.ScopeType == candidate.ScopeType
                && entry.ScopeValue.Equals(candidate.ScopeValue, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            await repository.AddAsync(candidate, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);
            return Map(candidate);
        }

        var mergedEngines = DiscoveryAllowlistPolicy.ParseEngines(existing.AllowedEngines);
        mergedEngines.UnionWith(DiscoveryAllowlistPolicy.ParseEngines(candidate.AllowedEngines));
        var existingPorts = DiscoveryAllowlistPolicy.ParsePorts(existing);
        var candidatePorts = DiscoveryAllowlistPolicy.ParsePorts(candidate);
        var mergedPorts = mergedEngines.ToDictionary(
            engine => engine.ToApiString(),
            engine => (IReadOnlyList<int>)(existingPorts.GetValueOrDefault(engine, [])
                .Concat(candidatePorts.GetValueOrDefault(engine, []))
                .Distinct()
                .Order()
                .ToArray()),
            StringComparer.OrdinalIgnoreCase);

        Build(
            new UpsertDiscoveryAllowlistCommand(
                string.IsNullOrWhiteSpace(existing.Name) ? candidate.Name : existing.Name,
                candidate.EnvironmentTag,
                TargetType(existing.ScopeType),
                candidate.ScopeValue,
                mergedEngines.Select(engine => engine.ToApiString()).ToArray(),
                mergedPorts,
                IsActive: true,
                existing.Description ?? candidate.Description),
            existing);
        await repository.SaveChangesAsync(cancellationToken);
        return Map(existing);
    }

    public async Task<DiscoveryAllowlistEntryDto> UpdateAsync(
        Guid id,
        UpsertDiscoveryAllowlistCommand command,
        CancellationToken cancellationToken = default)
    {
        var entry = await repository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Discovery allowlist entry was not found.");
        var candidate = Build(command, entry);
        await EnsureNotDuplicateAsync(candidate, id, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return Map(candidate);
    }

    public async Task<DiscoveryAllowlistEntryDto> SetActiveAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var entry = await repository.GetByIdAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Discovery allowlist entry was not found.");
        entry.Enabled = isActive;
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        await repository.SaveChangesAsync(cancellationToken);
        return Map(entry);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entry = await repository.GetByIdAsync(id, cancellationToken);
        if (entry is null)
        {
            return false;
        }

        await repository.DeleteAsync(entry, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);
        return true;
    }

    private ScanScopeAllowlist Build(UpsertDiscoveryAllowlistCommand command, ScanScopeAllowlist? entry)
    {
        var name = command.Name.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 160)
        {
            throw new InvalidOperationException("Allowlist name is required and must not exceed 160 characters.");
        }

        var environment = NormalizeEnvironment(command.Environment);
        var scopeType = ParseTargetType(command.TargetType);
        var targetValue = NormalizeTarget(scopeType, command.TargetValue);
        var engines = ParseEngines(command.AllowedEngines);
        var ports = NormalizePorts(engines, command.AllowedPorts);
        var now = DateTimeOffset.UtcNow;

        entry ??= new ScanScopeAllowlist { ScopeValue = targetValue, CreatedAt = now };
        entry.Name = name;
        entry.EnvironmentTag = environment;
        entry.ScopeType = scopeType;
        entry.ScopeValue = targetValue;
        entry.AllowedEngines = DiscoveryAllowlistPolicy.SerializeEngines(engines);
        entry.AllowedPorts = DiscoveryAllowlistPolicy.SerializePorts(ports);
        entry.Port = ports.SelectMany(pair => pair.Value).Distinct().Count() == 1
            ? ports.SelectMany(pair => pair.Value).Single()
            : null;
        entry.Enabled = command.IsActive;
        entry.Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description.Trim();
        entry.UpdatedAt = now;
        return entry;
    }

    private async Task EnsureNotDuplicateAsync(
        ScanScopeAllowlist candidate,
        Guid? excludedId,
        CancellationToken cancellationToken)
    {
        var entries = await repository.ListAsync(candidate.EnvironmentTag, cancellationToken);
        if (entries.Any(entry => entry.Id != excludedId
            && entry.ScopeType == candidate.ScopeType
            && entry.ScopeValue.Equals(candidate.ScopeValue, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("An allowlist entry already exists for this environment and target.");
        }
    }

    private static string NormalizeEnvironment(string value) =>
        DiscoveryEnvironments.TryNormalize(value, out var environment)
            ? environment
            : throw new InvalidOperationException(
                $"Unsupported environment. Allowed values: {string.Join(", ", DiscoveryEnvironments.All.Order())}.");

    private static ScanScopeType ParseTargetType(string value) => value.Trim().ToLowerInvariant() switch
    {
        "host" => ScanScopeType.Hostname,
        "ipv4" => ScanScopeType.IpAddress,
        "ipv4cidr" => ScanScopeType.Cidr,
        _ => throw new InvalidOperationException("TargetType must be Host, IPv4 or IPv4Cidr.")
    };

    private string NormalizeTarget(ScanScopeType type, string value)
    {
        var target = value.Trim().TrimEnd('.').ToLowerInvariant();
        if (target.Contains(':'))
        {
            throw new InvalidOperationException("IPv6 discovery is not currently supported.");
        }

        return type switch
        {
            ScanScopeType.Hostname when Uri.CheckHostName(target) == UriHostNameType.Dns => target,
            ScanScopeType.IpAddress when IPAddress.TryParse(target, out var address)
                && address.AddressFamily == AddressFamily.InterNetwork => address.ToString(),
            ScanScopeType.Cidr when TryNormalizeCidr(target, out var cidr) => cidr,
            _ => throw new InvalidOperationException("TargetValue is not valid for the selected TargetType.")
        };
    }

    private bool TryNormalizeCidr(string value, out string normalized)
    {
        normalized = string.Empty;
        if (!TryParseIpv4Cidr(value, out var network, out var prefix))
        {
            return false;
        }

        var addressCount = 1L << (32 - prefix);
        if (addressCount > configuration.MaxCidrHosts)
        {
            throw new InvalidOperationException(
                $"IPv4 CIDR cannot contain more than {configuration.MaxCidrHosts} addresses.");
        }

        normalized = $"{network}/{prefix}";
        return true;
    }

    private static bool TryParseIpv4Cidr(string value, out IPAddress network, out int prefix)
    {
        network = IPAddress.None;
        prefix = 0;
        var parts = value.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2
            || !IPAddress.TryParse(parts[0], out var address)
            || address.AddressFamily != AddressFamily.InterNetwork
            || !int.TryParse(parts[1], out prefix)
            || prefix is < 0 or > 32)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        var raw = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        var mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
        var result = raw & mask;
        network = new IPAddress(new[] { (byte)(result >> 24), (byte)(result >> 16), (byte)(result >> 8), (byte)result });
        return true;
    }

    private static HashSet<DatabaseEngine> ParseEngines(IEnumerable<string> values)
    {
        var engines = new HashSet<DatabaseEngine>();
        foreach (var value in values)
        {
            if (!DatabaseEngineExtensions.TryParseApiString(value, out var engine)
                || engine is not (DatabaseEngine.MySql or DatabaseEngine.PostgreSql or DatabaseEngine.MsSql or DatabaseEngine.Oracle))
            {
                throw new InvalidOperationException("AllowedEngines contains an unsupported database engine.");
            }

            engines.Add(engine);
        }

        return engines.Count > 0
            ? engines
            : throw new InvalidOperationException("At least one allowed engine is required.");
    }

    private IReadOnlyDictionary<DatabaseEngine, IReadOnlyList<int>> NormalizePorts(
        IReadOnlyCollection<DatabaseEngine> engines,
        IReadOnlyDictionary<string, IReadOnlyList<int>>? requested)
    {
        var normalized = new Dictionary<DatabaseEngine, IReadOnlyList<int>>();
        foreach (var engine in engines)
        {
            var requestedPorts = requested?.FirstOrDefault(pair =>
                pair.Key.Equals(engine.ToApiString(), StringComparison.OrdinalIgnoreCase)).Value ?? [];
            if (requestedPorts.Count != requestedPorts.Distinct().Count())
            {
                throw new InvalidOperationException("Allowed ports cannot contain duplicate values.");
            }

            var ports = requestedPorts.Append(DiscoveryAllowlistPolicy.StandardPorts[engine]).Distinct().Order().ToArray();
            if (ports.Any(port => port is < 1 or > 65535))
            {
                throw new InvalidOperationException("Allowed ports must be between 1 and 65535.");
            }

            normalized[engine] = ports;
        }

        if (normalized.Sum(pair => pair.Value.Count) > configuration.MaxPortsPerTarget)
        {
            throw new InvalidOperationException(
                $"An allowlist target cannot define more than {configuration.MaxPortsPerTarget} engine/port pairs.");
        }

        var duplicateAcrossEngines = normalized
            .SelectMany(pair => pair.Value.Select(port => new { pair.Key, Port = port }))
            .GroupBy(item => item.Port)
            .Any(group => group.Select(item => item.Key).Distinct().Count() > 1);
        if (duplicateAcrossEngines)
        {
            throw new InvalidOperationException("The same port cannot be assigned to more than one engine in an allowlist entry.");
        }

        var unsupportedKeys = requested?.Keys.Any(key =>
            !engines.Any(engine => engine.ToApiString().Equals(key, StringComparison.OrdinalIgnoreCase))) == true;
        if (unsupportedKeys)
        {
            throw new InvalidOperationException("AllowedPorts contains an engine that is not selected in AllowedEngines.");
        }

        return normalized;
    }

    private static DiscoveryAllowlistEntryDto Map(ScanScopeAllowlist entry)
    {
        var engines = DiscoveryAllowlistPolicy.ParseEngines(entry.AllowedEngines)
            .Select(engine => engine.ToApiString())
            .Order()
            .ToArray();
        var ports = DiscoveryAllowlistPolicy.ParsePorts(entry)
            .ToDictionary(
                pair => pair.Key.ToApiString(),
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
        var targetType = TargetType(entry.ScopeType);

        return new DiscoveryAllowlistEntryDto(
            entry.Id,
            string.IsNullOrWhiteSpace(entry.Name) ? entry.ScopeValue : entry.Name,
            entry.EnvironmentTag,
            targetType,
            entry.ScopeValue,
            engines,
            ports,
            entry.Enabled,
            entry.Description,
            entry.CreatedAt,
            entry.UpdatedAt);
    }

    private static string TargetType(ScanScopeType scopeType) => scopeType switch
        {
            ScanScopeType.IpAddress => "IPv4",
            ScanScopeType.Cidr => "IPv4Cidr",
            _ => "Host"
        };
}
