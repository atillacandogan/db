using DbScanner.Contracts.Scans;

namespace DbScanner.Application.Models;

public static class ScanRequestedMessageCompatibility
{
    public static ScanRequestedMessage Create(
        Guid scanId,
        Guid targetId,
        string engine,
        string host,
        int port,
        DateTimeOffset requestedAt,
        IReadOnlyCollection<string>? selectedCheckIds,
        string? protectedCredentials)
    {
        var normalizedCheckIds = selectedCheckIds?.ToArray();
        return new ScanRequestedMessage(
            scanId,
            targetId,
            engine,
            host,
            port,
            requestedAt,
            ProtectedCredentials: protectedCredentials,
            SelectedCheckIds: normalizedCheckIds);
    }

    public static IReadOnlyCollection<string>? ResolveSelection(ScanRequestedMessage message) =>
        ScanCheckSelectionCompatibility.Resolve(
            message.SelectedCheckIds,
            message.SelectedNseScriptIds);
}
