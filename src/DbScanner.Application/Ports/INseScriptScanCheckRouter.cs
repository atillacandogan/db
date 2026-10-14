using DbScanner.Application.Models;
using DbScanner.Domain.Entities;

namespace DbScanner.Application.Ports;

/// <summary>
/// Temporary migration port that assigns legacy NSE catalog entries to their
/// server-controlled executor strategies.
/// </summary>
public interface INseScriptScanCheckRouter
{
    ScanCheckDefinition Route(NseScript script);

    IReadOnlyList<ScanCheckDefinition> Route(IReadOnlyCollection<NseScript> scripts);
}
