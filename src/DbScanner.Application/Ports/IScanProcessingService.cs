using DbScanner.Application.Models;

namespace DbScanner.Application.Ports;

public interface IScanProcessingService
{
    Task ProcessAsync(Guid scanId, CancellationToken cancellationToken = default);

    Task ProcessAsync(
        Guid scanId,
        IReadOnlyCollection<string>? selectedCheckIds,
        CancellationToken cancellationToken);

    Task ProcessAsync(
        Guid scanId,
        IReadOnlyCollection<string>? selectedCheckIds,
        DatabaseAuthentication? authentication,
        CancellationToken cancellationToken);
}
