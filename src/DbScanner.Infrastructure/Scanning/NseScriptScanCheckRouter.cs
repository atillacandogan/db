using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;

namespace DbScanner.Infrastructure.Scanning;

internal sealed class NseScriptScanCheckRouter : INseScriptScanCheckRouter
{
    private const string MySqlEmptyPasswordScript = "mysql-empty-password";

    public ScanCheckDefinition Route(NseScript script)
    {
        ArgumentNullException.ThrowIfNull(script);

        var executorType = LegacyScanCheckExecutorRouting.Resolve(script);
        return script.ToScanCheckDefinition(executorType);
    }

    public IReadOnlyList<ScanCheckDefinition> Route(IReadOnlyCollection<NseScript> scripts)
    {
        ArgumentNullException.ThrowIfNull(scripts);
        var routedChecks = scripts.Select(Route).ToArray();
        var requiresMySqlCompatibilityPlan = routedChecks.Any(check =>
            string.Equals(
                check.Name,
                MySqlEmptyPasswordScript,
                StringComparison.OrdinalIgnoreCase));

        if (!requiresMySqlCompatibilityPlan)
        {
            return routedChecks;
        }

        // mysql-empty-password needs mysql-info preflight and may fall back to
        // its legacy NSE implementation. Co-locating compatible MySQL/Nmap
        // checks keeps that decision and the network scan atomic.
        return routedChecks
            .Select(check => CanJoinMySqlCompatibilityPlan(check)
                ? check with { ExecutorType = ScanExecutorTypes.MySqlNative }
                : check)
            .ToArray();
    }

    private static bool CanJoinMySqlCompatibilityPlan(ScanCheckDefinition check) =>
        check.ExecutorType is ScanExecutorTypes.NmapNse or ScanExecutorTypes.MySqlNative
        && check.SupportedEngines.Any(engine =>
            engine is DatabaseEngine.MySql or DatabaseEngine.Generic);
}
