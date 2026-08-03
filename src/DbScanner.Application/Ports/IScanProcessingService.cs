using DbScanner.Application.Models;

namespace DbScanner.Application.Ports;

public interface IScanProcessingService
{
    Task ProcessAsync(Guid scanId, CancellationToken cancellationToken = default);

    Task ProcessAsync(
        Guid scanId,
        IReadOnlyCollection<string>? selectedNseScriptIds,
        CancellationToken cancellationToken);

    Task ProcessAsync(
        Guid scanId,
        IReadOnlyCollection<string>? selectedNseScriptIds,
        DatabaseAuthentication? authentication,
        CancellationToken cancellationToken);
}
