using DbScanner.Contracts.Scans;

namespace DbScanner.Application.Models;

/// <summary>Scoped fencing identity. Only the consumer sets this for a claimed execution.</summary>
public sealed class ScanExecutionLeaseContext
{
    public Guid? ScanId { get; set; }
    public Guid? Token { get; set; }
}

public sealed record ScanJobLease(Guid ScanId, Guid Token, int Attempt, ScanRequestedMessage Request);

public sealed class ScanLeaseLostException(Guid scanId)
    : InvalidOperationException($"Execution lease for scan {scanId} is no longer owned.");
