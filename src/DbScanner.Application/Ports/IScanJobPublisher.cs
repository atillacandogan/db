using DbScanner.Application.Models;
using DbScanner.Domain.Entities;

namespace DbScanner.Application.Ports;

public interface IScanJobPublisher
{
    // Stages a durable outbox request. Caller commits it with the scan in the same unit of work.
    Task PublishScanRequestedAsync(Scan scan, Target target, CancellationToken cancellationToken = default);

    Task PublishScanRequestedAsync(
        Scan scan,
        Target target,
        IReadOnlyCollection<string>? selectedCheckIds,
        CancellationToken cancellationToken);

    Task PublishScanRequestedAsync(
        Scan scan,
        Target target,
        IReadOnlyCollection<string>? selectedCheckIds,
        DatabaseAuthentication? authentication,
        CancellationToken cancellationToken);
}
