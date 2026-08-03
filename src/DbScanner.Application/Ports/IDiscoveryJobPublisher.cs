using DbScanner.Domain.Entities;

namespace DbScanner.Application.Ports;

public interface IDiscoveryJobPublisher
{
    Task PublishRequestedAsync(DiscoveryJob job, CancellationToken cancellationToken = default);
}
