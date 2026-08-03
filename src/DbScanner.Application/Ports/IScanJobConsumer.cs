namespace DbScanner.Application.Ports;

public interface IScanJobConsumer
{
    Task StartAsync(CancellationToken cancellationToken = default);
}
