using DbScanner.Application.Models;
using DbScanner.Domain.Entities;

namespace DbScanner.Infrastructure.Scanning;

internal interface IOracleAuthenticatedProbe
{
    Task<IReadOnlyList<ScanScriptExecutionResult>> RunAsync(
        Target target,
        IReadOnlyCollection<NseScript> scripts,
        DatabaseAuthentication authentication,
        CancellationToken cancellationToken = default);
}
