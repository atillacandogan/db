using DbScanner.Application.Models;
using DbScanner.Domain.Entities;

namespace DbScanner.Application.Ports;

public interface IScanJobPublisher
{
    Task PublishScanRequestedAsync(Scan scan, Target target, CancellationToken cancellationToken = default);

    Task PublishScanRequestedAsync(
        Scan scan,
        Target target,
        IReadOnlyCollection<string>? selectedNseScriptIds,
        CancellationToken cancellationToken);

    Task PublishScanRequestedAsync(
        Scan scan,
        Target target,
        IReadOnlyCollection<string>? selectedNseScriptIds,
        DatabaseAuthentication? authentication,
        CancellationToken cancellationToken);
}
