using DbScanner.Domain.Entities;

namespace DbScanner.Application.Models;

/// <summary>
/// Common input passed to a scan executor. Credentials remain in the existing
/// protected application model and are never copied into execution results.
/// </summary>
public sealed record ScanExecutionContext(
    Scan Scan,
    Target Target,
    DatabaseAuthentication? Authentication = null,
    IReadOnlyCollection<string>? RequiredExecutorTypes = null);
