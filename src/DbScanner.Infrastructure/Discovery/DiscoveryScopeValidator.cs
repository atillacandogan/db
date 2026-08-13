using System.Net;
using System.Net.Sockets;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Contracts.Discovery;
using DbScanner.Domain;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using DbScanner.Domain.Services;
using Microsoft.Extensions.Options;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class DiscoveryScopeValidator(
    IScanScopeRepository scanScopeRepository,
    IDiscoveryDnsResolver dnsResolver,
    IOptions<DiscoveryOptions> options) : IDiscoveryScopeValidator
{
    public async Task<IReadOnlyList<DiscoveryEndpoint>> PlanAuthorizedAsync(
        string environment,
        IReadOnlySet<DatabaseEngine> supportedEngineFilter,
        CancellationToken cancellationToken = default)
    {
        var normalizedEnvironment = NormalizeEnvironment(environment);
        var allowlist = (await scanScopeRepository.ListAsync(normalizedEnvironment, cancellationToken))
            .Where(entry => entry.Enabled)
            .ToArray();
        var compatible = allowlist
            .Where(entry => DiscoveryAllowlistPolicy.ParseEngines(entry.AllowedEngines)
                .Overlaps(supportedEngineFilter))
            .ToArray();
        if (compatible.Length == 0)
        {
            throw new InvalidOperationException(
                $"No active discovery allowlist entries exist for environment '{normalizedEnvironment}' and the selected engines.");
        }

        var endpoints = new HashSet<DiscoveryEndpoint>();
        foreach (var entry in compatible)
        {
            var plan = await PlanEntryAsync(entry, allowlist, supportedEngineFilter, cancellationToken);
            if (plan.Status != DiscoveryTargetValidationStatus.Authorized)
            {
                throw new InvalidOperationException($"Allowlist entry '{entry.Name}' cannot be used: {plan.Message}");
            }

            endpoints.UnionWith(plan.Endpoints);
        }

        return endpoints
            .OrderBy(endpoint => endpoint.Host)
            .ThenBy(endpoint => endpoint.Port)
            .ThenBy(endpoint => endpoint.ConnectAddress)
            .ToArray();
    }

    public Task<IReadOnlyList<DiscoveryEndpoint>> PlanLocalNetworksAsync(
        IReadOnlyCollection<string> cidrs,
        IReadOnlySet<DatabaseEngine> supportedEngineFilter,
        CancellationToken cancellationToken = default)
    {
        if (cidrs.Count == 0)
        {
            throw new InvalidOperationException("At least one detected private IPv4 network must be selected.");
        }

        var endpoints = new HashSet<DiscoveryEndpoint>();
        long totalAddressCount = 0;
        foreach (var cidr in cidrs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!NetworkScope.TryParseIpv4Cidr(cidr, out var network, out var prefix))
            {
                throw new InvalidOperationException($"Detected local network '{cidr}' is not a valid IPv4 CIDR.");
            }

            if (!NetworkScope.IsPrivateNetwork(network))
            {
                throw new InvalidOperationException("Local network discovery accepts only RFC1918 private IPv4 networks.");
            }

            var addressCount = NetworkScope.GetIpv4AddressCount(prefix);
            totalAddressCount += addressCount;
            if (addressCount > options.Value.MaxCidrHosts || totalAddressCount > options.Value.MaxCidrHosts)
            {
                throw new InvalidOperationException(
                    $"Selected local networks cannot contain more than {options.Value.MaxCidrHosts} addresses in total.");
            }

            foreach (var address in NetworkScope.ExpandIpv4(network, prefix, options.Value.MaxCidrHosts))
            {
                foreach (var engine in supportedEngineFilter)
                {
                    foreach (var port in options.Value.GetLocalNetworkPorts(engine))
                    {
                        endpoints.Add(new DiscoveryEndpoint(address, port, engine, address));
                    }
                }

                foreach (var port in options.Value.GetUnsupportedDatabasePorts())
                {
                    endpoints.Add(new DiscoveryEndpoint(address, port, null, address));
                }
            }
        }

        return Task.FromResult<IReadOnlyList<DiscoveryEndpoint>>(
            endpoints.OrderBy(endpoint => endpoint.Host).ThenBy(endpoint => endpoint.Port).ToArray());
    }

    public async Task<IReadOnlyList<DiscoveryEndpoint>> ValidateAndPlanAsync(
        string scopeInput,
        string environment,
        IReadOnlySet<DatabaseEngine> supportedEngineFilter,
        CancellationToken cancellationToken = default)
    {
        var plan = await ValidateAndPlanDetailedAsync(
            scopeInput,
            environment,
            supportedEngineFilter,
            cancellationToken);
        if (plan.Endpoints.Count == 0)
        {
            var details = string.Join(
                " ",
                plan.Targets.Where(target => target.Status != DiscoveryTargetValidationStatus.Authorized)
                    .Select(target => $"{target.Target}: {target.Message}"));
            throw new InvalidOperationException($"Discovery scope validation failed. {details}".Trim());
        }

        return plan.Endpoints;
    }

    public async Task<DiscoveryScopePlan> ValidateAndPlanDetailedAsync(
        string scopeInput,
        string environment,
        IReadOnlySet<DatabaseEngine> supportedEngineFilter,
        CancellationToken cancellationToken = default)
    {
        _ = NormalizeEnvironment(environment);
        var tokens = SplitTargets(scopeInput);
        if (tokens.Length == 0)
        {
            return new DiscoveryScopePlan(
                [],
                [Result(string.Empty, DiscoveryTargetValidationStatus.InvalidFormat, "At least one host, IPv4 address or IPv4 CIDR is required.")]);
        }

        if (tokens.Length > options.Value.MaxCidrHosts)
        {
            return new DiscoveryScopePlan(
                [],
                tokens.Select(token => Result(
                    token,
                    DiscoveryTargetValidationStatus.CidrTooLarge,
                    $"A discovery request cannot contain more than {options.Value.MaxCidrHosts} target entries.")).ToArray());
        }

        var endpoints = new HashSet<DiscoveryEndpoint>();
        var results = new List<DiscoveryTargetValidationDto>(tokens.Length);
        var expandedCount = 0;
        foreach (var token in tokens)
        {
            var planned = await ValidateTargetAsync(
                token,
                supportedEngineFilter,
                cancellationToken);
            results.Add(new DiscoveryTargetValidationDto(
                token,
                planned.Status,
                planned.Message,
                planned.ResolvedAddresses));
            if (planned.Status == DiscoveryTargetValidationStatus.Authorized)
            {
                expandedCount += planned.ExpandedHostCount;
                if (expandedCount > options.Value.MaxCidrHosts)
                {
                    results[^1] = Result(
                        token,
                        DiscoveryTargetValidationStatus.CidrTooLarge,
                        $"The combined discovery scope cannot expand beyond {options.Value.MaxCidrHosts} hosts.");
                    continue;
                }

                endpoints.UnionWith(planned.Endpoints);
            }
        }

        return new DiscoveryScopePlan(
            endpoints.OrderBy(endpoint => endpoint.Host).ThenBy(endpoint => endpoint.Port).ToArray(),
            results);
    }

    private async Task<PlannedTarget> ValidateTargetAsync(
        string token,
        IReadOnlySet<DatabaseEngine> engines,
        CancellationToken cancellationToken)
    {
        if (token.Contains(':'))
        {
            return PlannedTarget.Failed(
                DiscoveryTargetValidationStatus.UnsupportedAddressType,
                "IPv6 discovery is not currently supported.");
        }

        if (token.Contains('/'))
        {
            return ValidateExplicitCidr(token, engines);
        }

        if (IPAddress.TryParse(token, out var address))
        {
            return address.AddressFamily == AddressFamily.InterNetwork
                ? ValidateExplicitIp(address, engines)
                : PlannedTarget.Failed(
                    DiscoveryTargetValidationStatus.UnsupportedAddressType,
                    "IPv6 discovery is not currently supported.");
        }

        if (Uri.CheckHostName(token) != UriHostNameType.Dns)
        {
            return PlannedTarget.Failed(
                DiscoveryTargetValidationStatus.InvalidFormat,
                "Target is not a valid hostname, IPv4 address or IPv4 CIDR.");
        }

        return await ValidateExplicitHostnameAsync(token, engines, cancellationToken);
    }

    private PlannedTarget ValidateExplicitIp(
        IPAddress address,
        IReadOnlySet<DatabaseEngine> engines)
    {
        if (!NetworkScope.IsPrivateUnicast(address))
        {
            return PlannedTarget.Failed(
                DiscoveryTargetValidationStatus.UnsupportedAddressType,
                "Manual discovery accepts only private unicast IPv4 addresses.");
        }

        return PlannedTarget.Authorized(
            BuildConfiguredEndpoints(address.ToString(), null, engines),
            1,
            [address]);
    }

    private PlannedTarget ValidateExplicitCidr(
        string token,
        IReadOnlySet<DatabaseEngine> engines)
    {
        if (!NetworkScope.TryParseIpv4Cidr(token, out var network, out var prefix))
        {
            return PlannedTarget.Failed(
                DiscoveryTargetValidationStatus.InvalidFormat,
                "Target is not a valid IPv4 CIDR.");
        }

        var addressCount = NetworkScope.GetIpv4AddressCount(prefix);
        if (addressCount > options.Value.MaxCidrHosts)
        {
            return PlannedTarget.Failed(
                DiscoveryTargetValidationStatus.CidrTooLarge,
                $"IPv4 CIDR cannot contain more than {options.Value.MaxCidrHosts} addresses.");
        }

        if (!NetworkScope.IsPrivateNetwork(network))
        {
            return PlannedTarget.Failed(
                DiscoveryTargetValidationStatus.UnsupportedAddressType,
                "Manual discovery accepts only private IPv4 CIDR ranges.");
        }

        var addresses = NetworkScope.ExpandIpv4(network, prefix, options.Value.MaxCidrHosts);
        return PlannedTarget.Authorized(
            addresses.SelectMany(address => BuildConfiguredEndpoints(address, null, engines)),
            addresses.Count,
            addresses.Select(IPAddress.Parse));
    }

    private async Task<PlannedTarget> ValidateExplicitHostnameAsync(
        string hostname,
        IReadOnlySet<DatabaseEngine> engines,
        CancellationToken cancellationToken)
    {
        var normalizedHostname = hostname.Trim().TrimEnd('.');
        var addresses = await dnsResolver.ResolveIpv4Async(normalizedHostname, cancellationToken);
        if (addresses.Count == 0)
        {
            return PlannedTarget.Failed(
                DiscoveryTargetValidationStatus.DnsResolutionFailed,
                "Hostname could not be resolved to an IPv4 address.");
        }

        if (addresses.Any(address => !NetworkScope.IsPrivateUnicast(address)))
        {
            return PlannedTarget.Failed(
                DiscoveryTargetValidationStatus.ResolvedAddressNotAuthorized,
                "Hostname resolved to a public, loopback, link-local, multicast or otherwise unsafe IPv4 address.",
                addresses);
        }

        return PlannedTarget.Authorized(
            addresses.SelectMany(address =>
                BuildConfiguredEndpoints(normalizedHostname, address.ToString(), engines)),
            addresses.Count,
            addresses);
    }

    private IEnumerable<DiscoveryEndpoint> BuildConfiguredEndpoints(
        string host,
        string? connectAddress,
        IReadOnlySet<DatabaseEngine> engines) =>
        engines.SelectMany(engine => options.Value.GetLocalNetworkPorts(engine)
            .Select(port => new DiscoveryEndpoint(host, port, engine, connectAddress)))
        .Concat(options.Value.GetUnsupportedDatabasePorts()
            .Select(port => new DiscoveryEndpoint(host, port, null, connectAddress)));

    private async Task<PlannedTarget> ValidateHostnameAsync(
        string hostname,
        string environment,
        IReadOnlySet<DatabaseEngine> engines,
        IReadOnlyList<ScanScopeAllowlist> allEntries,
        CancellationToken cancellationToken)
    {
        var hostEntries = allEntries.Where(entry =>
            entry.ScopeType is ScanScopeType.Hostname or ScanScopeType.DemoTarget
            && entry.ScopeValue.Equals(hostname.Trim().TrimEnd('.'), StringComparison.OrdinalIgnoreCase)).ToArray();
        var authorization = SelectAuthorizedEntries(hostEntries, environment, engines);
        if (authorization.Error is not null)
        {
            return authorization.Error;
        }

        var addresses = await dnsResolver.ResolveIpv4Async(hostname, cancellationToken);
        if (addresses.Count == 0)
        {
            return PlannedTarget.Failed(
                DiscoveryTargetValidationStatus.DnsResolutionFailed,
                "Hostname could not be resolved to an IPv4 address.");
        }

        if (addresses.Any(address => !NetworkScope.IsPrivateUnicast(address)))
        {
            return PlannedTarget.Failed(
                DiscoveryTargetValidationStatus.ResolvedAddressNotAuthorized,
                "Hostname resolved to a public, loopback, link-local, multicast or otherwise unsafe IPv4 address.",
                addresses);
        }

        var endpoints = new HashSet<DiscoveryEndpoint>();
        var legacyDemoEntry = authorization.Entries.Any(entry => entry.ScopeType == ScanScopeType.DemoTarget);
        foreach (var address in addresses)
        {
            foreach (var hostEntry in authorization.Entries)
            {
                foreach (var engine in GetCompatibleEngines(hostEntry, engines))
                {
                    foreach (var port in NetworkScope.GetPorts(hostEntry, engine))
                    {
                        var addressAuthorized = legacyDemoEntry || allEntries.Any(entry =>
                            entry.Enabled
                            && entry.EnvironmentTag.Equals(environment, StringComparison.OrdinalIgnoreCase)
                            && entry.ScopeType is ScanScopeType.IpAddress or ScanScopeType.Cidr
                            && NetworkScope.EntryContainsHost(entry, address.ToString())
                            && NetworkScope.EntryAllowsEnginePort(entry, engine, port));
                        if (addressAuthorized)
                        {
                            endpoints.Add(new DiscoveryEndpoint(hostname, port, engine, address.ToString()));
                        }
                    }
                }
            }

            if (!endpoints.Any(endpoint => endpoint.ConnectAddress == address.ToString()))
            {
                return PlannedTarget.Failed(
                    DiscoveryTargetValidationStatus.ResolvedAddressNotAuthorized,
                    $"Resolved IPv4 address {address} is not separately authorized for the selected environment, engines and ports.",
                    addresses);
            }
        }

        return PlannedTarget.Authorized(endpoints, 1, addresses);
    }

    private PlannedTarget ValidateIp(
        IPAddress address,
        string environment,
        IReadOnlySet<DatabaseEngine> engines,
        IReadOnlyList<ScanScopeAllowlist> allEntries)
    {
        var matching = allEntries.Where(entry =>
            entry.ScopeType is ScanScopeType.IpAddress or ScanScopeType.Cidr
            && NetworkScope.EntryContainsHost(entry, address.ToString())).ToArray();
        var authorization = SelectAuthorizedEntries(matching, environment, engines);
        if (authorization.Error is not null)
        {
            return authorization.Error;
        }

        return PlannedTarget.Authorized(
            BuildEndpoints(address.ToString(), null, authorization.Entries, engines),
            1,
            [address]);
    }

    private PlannedTarget ValidateCidr(
        string token,
        string environment,
        IReadOnlySet<DatabaseEngine> engines,
        IReadOnlyList<ScanScopeAllowlist> allEntries)
    {
        if (!NetworkScope.TryParseIpv4Cidr(token, out var requestedNetwork, out var requestedPrefix))
        {
            return PlannedTarget.Failed(
                DiscoveryTargetValidationStatus.InvalidFormat,
                "Target is not a valid IPv4 CIDR.");
        }

        var addressCount = NetworkScope.GetIpv4AddressCount(requestedPrefix);
        if (addressCount > options.Value.MaxCidrHosts)
        {
            return PlannedTarget.Failed(
                DiscoveryTargetValidationStatus.CidrTooLarge,
                $"IPv4 CIDR cannot contain more than {options.Value.MaxCidrHosts} addresses.");
        }

        if (!NetworkScope.IsPrivateNetwork(requestedNetwork))
        {
            return PlannedTarget.Failed(
                DiscoveryTargetValidationStatus.NotInAllowlist,
                "Public, loopback, link-local and multicast CIDR ranges are not permitted for discovery.");
        }

        var matching = allEntries.Where(entry =>
            entry.ScopeType == ScanScopeType.Cidr
            && NetworkScope.TryParseIpv4Cidr(entry.ScopeValue, out var allowedNetwork, out var allowedPrefix)
            && NetworkScope.ContainsCidr(allowedNetwork, allowedPrefix, requestedNetwork, requestedPrefix)).ToArray();
        var authorization = SelectAuthorizedEntries(matching, environment, engines);
        if (authorization.Error is not null)
        {
            return authorization.Error;
        }

        var addresses = NetworkScope.ExpandIpv4(requestedNetwork, requestedPrefix, options.Value.MaxCidrHosts);
        var endpoints = addresses.SelectMany(address =>
            BuildEndpoints(address, null, authorization.Entries, engines)).ToArray();
        return PlannedTarget.Authorized(endpoints, addresses.Count, addresses.Select(IPAddress.Parse));
    }

    private async Task<PlannedTarget> PlanEntryAsync(
        ScanScopeAllowlist entry,
        IReadOnlyList<ScanScopeAllowlist> environmentEntries,
        IReadOnlySet<DatabaseEngine> engines,
        CancellationToken cancellationToken)
    {
        if (entry.ScopeType is ScanScopeType.Hostname or ScanScopeType.DemoTarget)
        {
            return await ValidateHostnameAsync(
                entry.ScopeValue,
                entry.EnvironmentTag,
                engines,
                environmentEntries,
                cancellationToken);
        }

        if (entry.ScopeType == ScanScopeType.IpAddress
            && IPAddress.TryParse(entry.ScopeValue, out var address))
        {
            return PlannedTarget.Authorized(
                BuildEndpoints(entry.ScopeValue, null, [entry], engines),
                1,
                [address]);
        }

        if (entry.ScopeType == ScanScopeType.Cidr
            && NetworkScope.TryParseIpv4Cidr(entry.ScopeValue, out var network, out var prefix))
        {
            if (NetworkScope.GetIpv4AddressCount(prefix) > options.Value.MaxCidrHosts)
            {
                return PlannedTarget.Failed(
                    DiscoveryTargetValidationStatus.CidrTooLarge,
                    $"IPv4 CIDR cannot contain more than {options.Value.MaxCidrHosts} addresses.");
            }

            if (!NetworkScope.IsPrivateNetwork(network))
            {
                return PlannedTarget.Failed(
                    DiscoveryTargetValidationStatus.NotInAllowlist,
                    "Public, loopback, link-local and multicast CIDR ranges are not permitted for discovery.");
            }

            var addresses = NetworkScope.ExpandIpv4(network, prefix, options.Value.MaxCidrHosts);
            return PlannedTarget.Authorized(
                addresses.SelectMany(addressValue => BuildEndpoints(addressValue, null, [entry], engines)),
                addresses.Count,
                addresses.Select(IPAddress.Parse));
        }

        return PlannedTarget.Failed(
            DiscoveryTargetValidationStatus.InvalidFormat,
            "Allowlist target has an invalid format.");
    }

    private static IReadOnlyList<DiscoveryEndpoint> BuildEndpoints(
        string host,
        string? connectAddress,
        IEnumerable<ScanScopeAllowlist> entries,
        IReadOnlySet<DatabaseEngine> selectedEngines) =>
        entries.SelectMany(entry => GetCompatibleEngines(entry, selectedEngines)
                .SelectMany(engine => NetworkScope.GetPorts(entry, engine)
                    .Select(port => new DiscoveryEndpoint(host, port, engine, connectAddress))))
            .Distinct()
            .ToArray();

    private static IEnumerable<DatabaseEngine> GetCompatibleEngines(
        ScanScopeAllowlist entry,
        IReadOnlySet<DatabaseEngine> selectedEngines) =>
        DiscoveryAllowlistPolicy.ParseEngines(entry.AllowedEngines).Where(selectedEngines.Contains);

    private static AuthorizationSelection SelectAuthorizedEntries(
        IReadOnlyList<ScanScopeAllowlist> matching,
        string environment,
        IReadOnlySet<DatabaseEngine> engines)
    {
        if (matching.Count == 0)
        {
            return AuthorizationSelection.Failed(
                DiscoveryTargetValidationStatus.NotInAllowlist,
                "Target is not present in the discovery allowlist.");
        }

        var sameEnvironment = matching.Where(entry =>
            entry.EnvironmentTag.Equals(environment, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (sameEnvironment.Length == 0)
        {
            return AuthorizationSelection.Failed(
                DiscoveryTargetValidationStatus.EnvironmentMismatch,
                $"Target is not authorized for environment '{environment}'.");
        }

        var compatible = sameEnvironment.Where(entry =>
            DiscoveryAllowlistPolicy.ParseEngines(entry.AllowedEngines).Overlaps(engines)).ToArray();
        if (compatible.Length == 0)
        {
            return AuthorizationSelection.Failed(
                DiscoveryTargetValidationStatus.NotInAllowlist,
                "Target is not authorized for any selected database engine.");
        }

        var active = compatible.Where(entry => entry.Enabled).ToArray();
        return active.Length > 0
            ? new AuthorizationSelection(active, null)
            : AuthorizationSelection.Failed(
                DiscoveryTargetValidationStatus.InactiveAllowlist,
                "The matching discovery allowlist entry is inactive.");
    }

    private static string[] SplitTargets(string scopeInput) => scopeInput.Split(
        [',', ';', '\r', '\n', '\t', ' '],
        StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string NormalizeEnvironment(string value) =>
        DiscoveryEnvironments.TryNormalize(value, out var environment)
            ? environment
            : throw new InvalidOperationException(
                $"Unsupported environment. Allowed values: {string.Join(", ", DiscoveryEnvironments.All.Order())}.");

    private static DiscoveryTargetValidationDto Result(string target, string status, string message) =>
        new(target, status, message, []);

    private sealed record AuthorizationSelection(
        IReadOnlyList<ScanScopeAllowlist> Entries,
        PlannedTarget? Error)
    {
        public static AuthorizationSelection Failed(string status, string message) =>
            new([], PlannedTarget.Failed(status, message));
    }

    private sealed record PlannedTarget(
        string Status,
        string Message,
        IReadOnlyList<DiscoveryEndpoint> Endpoints,
        IReadOnlyList<string> ResolvedAddresses,
        int ExpandedHostCount)
    {
        public static PlannedTarget Failed(
            string status,
            string message,
            IEnumerable<IPAddress>? addresses = null) =>
            new(status, message, [], addresses?.Select(address => address.ToString()).ToArray() ?? [], 0);

        public static PlannedTarget Authorized(
            IEnumerable<DiscoveryEndpoint> endpoints,
            int expandedHostCount,
            IEnumerable<IPAddress>? addresses = null) =>
            new(
                DiscoveryTargetValidationStatus.Authorized,
                "Target is authorized for discovery.",
                endpoints.Distinct().ToArray(),
                addresses?.Select(address => address.ToString()).ToArray() ?? [],
                expandedHostCount);
    }
}
