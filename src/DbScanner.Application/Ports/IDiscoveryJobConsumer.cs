namespace DbScanner.Application.Ports;

public interface IDiscoveryJobConsumer
{
    Task StartAsync(CancellationToken cancellationToken = default);
}
