namespace DbScanner.Application.Ports;

public interface IDiscoveryAgent
{
    Task ExecuteAsync(Guid discoveryJobId, CancellationToken cancellationToken = default);
}
