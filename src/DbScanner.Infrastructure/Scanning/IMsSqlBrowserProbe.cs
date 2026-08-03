using DbScanner.Application.Models;
using DbScanner.Domain.Entities;

namespace DbScanner.Infrastructure.Scanning;

internal interface IMsSqlBrowserProbe
{
    Task<ScanScriptExecutionResult> RunAsync(
        Target target,
        NseScript script,
        CancellationToken cancellationToken = default);
}
