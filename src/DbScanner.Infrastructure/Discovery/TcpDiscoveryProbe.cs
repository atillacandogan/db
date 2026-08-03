using System.Net.Sockets;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using Microsoft.Extensions.Options;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class TcpDiscoveryProbe(IOptions<DiscoveryOptions> options) : IDiscoveryProbe
{
    public async Task<DiscoveryProbeResult> ProbeAsync(
        DiscoveryEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient();
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(options.Value.ConnectTimeoutMilliseconds);

        try
        {
            await client.ConnectAsync(endpoint.Host, endpoint.Port, connectTimeout.Token);
        }
        catch (SocketException)
        {
            return new DiscoveryProbeResult(endpoint.Host, endpoint.Port, false, []);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DiscoveryProbeResult(endpoint.Host, endpoint.Port, false, []);
        }

        var banner = await ReadPassiveBannerAsync(client, cancellationToken);
        return new DiscoveryProbeResult(endpoint.Host, endpoint.Port, true, banner);
    }

    private async Task<byte[]> ReadPassiveBannerAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var buffer = new byte[options.Value.MaxBannerBytes];
        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readTimeout.CancelAfter(options.Value.BannerReadTimeoutMilliseconds);

        try
        {
            var bytesRead = await client.GetStream().ReadAsync(buffer, readTimeout.Token);
            return buffer[..bytesRead];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
    }
}
