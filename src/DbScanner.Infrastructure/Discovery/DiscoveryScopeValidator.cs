using System.Net;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using Microsoft.Extensions.Options;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class DiscoveryScopeValidator(
    IScanScopeRepository scanScopeRepository,
    IOptions<DiscoveryOptions> options) : IDiscoveryScopeValidator
{
    private static readonly IReadOnlyDictionary<DatabaseEngine, int> SupportedPorts =
        new Dictionary<DatabaseEngine, int>
        {
            [DatabaseEngine.MySql] = 3306,
            [DatabaseEngine.PostgreSql] = 5432,
            [DatabaseEngine.MsSql] = 1433,
            [DatabaseEngine.Oracle] = 1521
        };

    private static readonly int[] DetectionOnlyPorts = [27017, 6379];

    public async Task<IReadOnlyList<DiscoveryEndpoint>> ValidateAndPlanAsync(
        string scopeInput,
        IReadOnlySet<DatabaseEngine> supportedEngineFilter,
        CancellationToken cancellationToken = default)
    {
        var tokens = scopeInput.Split(
            [',', ';', '\r', '\n', '\t', ' '],
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            throw new InvalidOperationException("Discovery scope must contain at least one host, IP address or CIDR.");
        }

        if (tokens.Length > options.Value.MaxHosts)
        {
            throw new InvalidOperationException($"Discovery scope cannot contain more than {options.Value.MaxHosts} entries.");
        }

        var allowlist = await scanScopeRepository.ListEnabledAsync(cancellationToken);
        var ports = supportedEngineFilter
            .Select(engine => SupportedPorts[engine])
            .Concat(DetectionOnlyPorts)
            .Distinct()
            .ToArray();
        var endpoints = new HashSet<DiscoveryEndpoint>();
        var expandedHostCount = 0;

        foreach (var token in tokens.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (token.Contains('/'))
            {
                AddCidrEndpoints(token, ports, allowlist, endpoints, ref expandedHostCount);
            }
            else
            {
                expandedHostCount++;
                if (expandedHostCount > options.Value.MaxHosts)
                {
                    throw new InvalidOperationException($"Discovery scope cannot expand beyond {options.Value.MaxHosts} hosts.");
                }

                AddAuthorizedPorts(token, ports, allowlist, endpoints);
            }
        }

        if (endpoints.Count == 0)
        {
            throw new InvalidOperationException("Discovery scope is outside the approved allowlist.");
        }

        return endpoints.OrderBy(endpoint => endpoint.Host).ThenBy(endpoint => endpoint.Port).ToArray();
    }

    private void AddCidrEndpoints(
        string token,
        IReadOnlyCollection<int> ports,
        IReadOnlyList<ScanScopeAllowlist> allowlist,
        ISet<DiscoveryEndpoint> endpoints,
        ref int expandedHostCount)
    {
        if (!NetworkScope.TryParseIpv4Cidr(token, out var requestedNetwork, out var requestedPrefix))
        {
            throw new InvalidOperationException("Discovery CIDR must be a valid IPv4 network.");
        }

        if (!NetworkScope.IsPrivateOrLocal(requestedNetwork))
        {
            throw new InvalidOperationException("Wide public internet ranges are not permitted for discovery.");
        }

        var containingScopes = allowlist.Where(scope =>
            scope.ScopeType == ScanScopeType.Cidr
            && NetworkScope.TryParseIpv4Cidr(scope.ScopeValue, out var allowedNetwork, out var allowedPrefix)
            && NetworkScope.ContainsCidr(allowedNetwork, allowedPrefix, requestedNetwork, requestedPrefix)).ToArray();
        if (containingScopes.Length == 0)
        {
            throw new InvalidOperationException("Discovery CIDR is outside the approved allowlist.");
        }

        var addresses = NetworkScope.ExpandIpv4(requestedNetwork, requestedPrefix, options.Value.MaxHosts);
        expandedHostCount += addresses.Count;
        if (expandedHostCount > options.Value.MaxHosts)
        {
            throw new InvalidOperationException($"Discovery scope cannot expand beyond {options.Value.MaxHosts} hosts.");
        }

        foreach (var address in addresses)
        {
            AddAuthorizedPorts(address, ports, containingScopes, endpoints);
        }
    }

    private static void AddAuthorizedPorts(
        string host,
        IEnumerable<int> ports,
        IEnumerable<ScanScopeAllowlist> allowlist,
        ISet<DiscoveryEndpoint> endpoints)
    {
        foreach (var port in ports)
        {
            if (NetworkScope.IsHostAllowed(host, port, allowlist))
            {
                endpoints.Add(new DiscoveryEndpoint(host, port));
            }
        }
    }
}
