using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using DbScanner.Domain.Enums;
using Microsoft.Extensions.Options;

namespace DbScanner.Infrastructure.Discovery;

internal sealed class TcpDiscoveryProbe(IOptions<DiscoveryOptions> options) : IDiscoveryProbe
{
    internal const string MySqlHandshakeEvidence = "mysql-handshake-v10";
    internal const string PostgreSqlSslEvidence = "postgresql-ssl-response";
    internal const string MsSqlPreLoginEvidence = "mssql-tds-prelogin";
    internal const string OracleTnsEvidence = "oracle-tns-listener";

    private static readonly DatabaseEngine[] SupportedProtocolOrder =
        [DatabaseEngine.MySql, DatabaseEngine.PostgreSql, DatabaseEngine.MsSql, DatabaseEngine.Oracle];

    private static readonly byte[] PostgreSqlSslRequestPacket =
        [0x00, 0x00, 0x00, 0x08, 0x04, 0xD2, 0x16, 0x2F];

    public async Task<DiscoveryProbeResult> ProbeAsync(
        DiscoveryEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        var isOpen = false;
        foreach (var engine in GetProtocolProbeOrder(endpoint))
        {
            var attempt = await ProbeProtocolAsync(endpoint, engine, cancellationToken);
            if (engine == DatabaseEngine.MsSql && attempt.Retryable)
            {
                var retry = await ProbeProtocolAsync(endpoint, engine, cancellationToken);
                if (retry.Connected)
                {
                    attempt = retry;
                }
            }

            if (!attempt.Connected)
            {
                if (!isOpen)
                {
                    return Closed(endpoint);
                }

                continue;
            }

            isOpen = true;
            if (attempt.Evidence is not null)
            {
                return new DiscoveryProbeResult(
                    endpoint.Host,
                    endpoint.Port,
                    true,
                    attempt.Response,
                    endpoint.ExpectedEngine,
                    attempt.Evidence);
            }
        }

        return isOpen ? OpenWithoutEvidence(endpoint) : Closed(endpoint);
    }

    private async Task<ProtocolProbeAttempt> ProbeProtocolAsync(
        DiscoveryEndpoint endpoint,
        DatabaseEngine engine,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        if (!await TryConnectAsync(client, endpoint, cancellationToken))
        {
            return ProtocolProbeAttempt.ConnectionFailed;
        }

        return engine switch
        {
            DatabaseEngine.MySql => await ReadProtocolGreetingAsync(
                client,
                IsMySqlHandshakeResponse,
                MySqlHandshakeEvidence,
                cancellationToken),
            DatabaseEngine.PostgreSql => await ExchangeProtocolProbeAsync(
                client,
                PostgreSqlSslRequestPacket,
                IsPostgreSqlSslResponse,
                PostgreSqlSslEvidence,
                minimumBytes: 1,
                cancellationToken),
            DatabaseEngine.MsSql => await ProbeMsSqlAsync(
                client,
                cancellationToken),
            DatabaseEngine.Oracle => await ExchangeProtocolProbeAsync(
                client,
                CreateOracleTnsVersionPacket(),
                IsOracleTnsResponse,
                OracleTnsEvidence,
                minimumBytes: 8,
                cancellationToken),
            _ => new ProtocolProbeAttempt(true, [], null)
        };
    }

    private async Task<bool> TryConnectAsync(
        TcpClient client,
        DiscoveryEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectTimeout.CancelAfter(options.Value.ConnectTimeoutMilliseconds);

        try
        {
            await client.ConnectAsync(endpoint.ConnectAddress ?? endpoint.Host, endpoint.Port, connectTimeout.Token);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task<ProtocolProbeAttempt> ReadProtocolGreetingAsync(
        TcpClient client,
        Func<byte[], bool> validator,
        string evidence,
        CancellationToken cancellationToken)
    {
        var response = await ReadLimitedAsync(client, cancellationToken, minimumBytes: 32);
        return new ProtocolProbeAttempt(true, response, validator(response) ? evidence : null);
    }

    private async Task<ProtocolProbeAttempt> ExchangeProtocolProbeAsync(
        TcpClient client,
        byte[] request,
        Func<byte[], bool> validator,
        string evidence,
        int minimumBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.GetStream().WriteAsync(request, cancellationToken);
            var response = await ReadLimitedAsync(client, cancellationToken, minimumBytes);
            return new ProtocolProbeAttempt(true, response, validator(response) ? evidence : null);
        }
        catch (IOException)
        {
            return new ProtocolProbeAttempt(true, [], null);
        }
        catch (SocketException)
        {
            return new ProtocolProbeAttempt(true, [], null);
        }
    }

    private async Task<ProtocolProbeAttempt> ProbeMsSqlAsync(
        TcpClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            await client.GetStream().WriteAsync(TdsPreLoginProtocol.CreateRequest(), cancellationToken);
            var readResult = await ReadTdsPacketAsync(client, cancellationToken);
            return new ProtocolProbeAttempt(
                true,
                readResult.Response,
                IsMsSqlPreLoginResponse(readResult.Response) ? MsSqlPreLoginEvidence : null,
                readResult.Retryable);
        }
        catch (IOException)
        {
            return new ProtocolProbeAttempt(true, [], null, true);
        }
        catch (SocketException)
        {
            return new ProtocolProbeAttempt(true, [], null, true);
        }
    }

    private async Task<TdsPacketReadResult> ReadTdsPacketAsync(
        TcpClient client,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[options.Value.MaxBannerBytes];
        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readTimeout.CancelAfter(options.Value.BannerReadTimeoutMilliseconds);

        try
        {
            var totalBytesRead = 0;
            while (totalBytesRead < 8)
            {
                var bytesRead = await client.GetStream().ReadAsync(
                    buffer.AsMemory(totalBytesRead, 8 - totalBytesRead),
                    readTimeout.Token);
                if (bytesRead == 0)
                {
                    return new TdsPacketReadResult([], true);
                }

                totalBytesRead += bytesRead;
            }

            if (!TdsPreLoginProtocol.TryGetDeclaredPacketLength(buffer.AsSpan(0, totalBytesRead), out var packetLength)
                || packetLength > buffer.Length)
            {
                return new TdsPacketReadResult(buffer[..totalBytesRead], false);
            }

            while (totalBytesRead < packetLength)
            {
                var bytesRead = await client.GetStream().ReadAsync(
                    buffer.AsMemory(totalBytesRead, packetLength - totalBytesRead),
                    readTimeout.Token);
                if (bytesRead == 0)
                {
                    return new TdsPacketReadResult([], true);
                }

                totalBytesRead += bytesRead;
            }

            return new TdsPacketReadResult(buffer[..packetLength], false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new TdsPacketReadResult([], true);
        }
        catch (IOException)
        {
            return new TdsPacketReadResult([], true);
        }
    }

    private async Task<byte[]> ReadLimitedAsync(
        TcpClient client,
        CancellationToken cancellationToken,
        int minimumBytes)
    {
        var buffer = new byte[options.Value.MaxBannerBytes];
        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readTimeout.CancelAfter(options.Value.BannerReadTimeoutMilliseconds);

        try
        {
            var totalBytesRead = 0;
            while (totalBytesRead < buffer.Length)
            {
                var bytesRead = await client.GetStream().ReadAsync(
                    buffer.AsMemory(totalBytesRead),
                    readTimeout.Token);
                if (bytesRead == 0)
                {
                    break;
                }

                totalBytesRead += bytesRead;
                if (totalBytesRead >= minimumBytes)
                {
                    break;
                }
            }

            return buffer[..totalBytesRead];
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

    private static IReadOnlyList<DatabaseEngine> GetProtocolProbeOrder(DiscoveryEndpoint endpoint)
    {
        var preferred = endpoint.ExpectedEngine ?? DefaultEngineForPort(endpoint.Port);
        return preferred is null
            ? SupportedProtocolOrder
            : [preferred.Value, .. SupportedProtocolOrder.Where(engine => engine != preferred.Value)];
    }

    private static DatabaseEngine? DefaultEngineForPort(int port) => port switch
    {
        3306 => DatabaseEngine.MySql,
        5432 => DatabaseEngine.PostgreSql,
        1433 => DatabaseEngine.MsSql,
        1521 => DatabaseEngine.Oracle,
        _ => null
    };

    internal static bool IsMySqlHandshakeResponse(byte[] response)
    {
        if (response.Length < 10 || response[3] != 0 || response[4] != 10)
        {
            return false;
        }

        var declaredPayloadLength = response[0] | response[1] << 8 | response[2] << 16;
        var serverVersionTerminator = Array.IndexOf(response, (byte)0, 5);
        return declaredPayloadLength >= 20
            && response.Length >= Math.Min(declaredPayloadLength + 4, 32)
            && serverVersionTerminator > 5;
    }

    internal static bool IsPostgreSqlSslResponse(byte[] response) =>
        response.Length == 1 && (response[0] == (byte)'S' || response[0] == (byte)'N');

    internal static bool IsMsSqlPreLoginResponse(byte[] response) =>
        TdsPreLoginProtocol.IsValidResponse(response);

    internal static bool IsOracleTnsResponse(byte[] response) =>
        response.Length >= 8
        && response[4] is 2 or 4 or 5 or 6 or 11
        && BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(0, 2)) >= 8;

    internal static byte[] CreateOracleTnsVersionPacket()
    {
        var connectData = Encoding.ASCII.GetBytes("(CONNECT_DATA=(COMMAND=version))");
        var packet = new byte[34 + connectData.Length];
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(0, 2), checked((ushort)packet.Length));
        packet[4] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(8, 2), 308);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(10, 2), 300);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(14, 2), 2048);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(16, 2), 32767);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(18, 2), 20376);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(22, 2), 1);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(24, 2), checked((ushort)connectData.Length));
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(26, 2), 34);
        packet[32] = 1;
        packet[33] = 1;
        connectData.CopyTo(packet, 34);
        return packet;
    }

    private static DiscoveryProbeResult OpenWithoutEvidence(DiscoveryEndpoint endpoint) =>
        new(endpoint.Host, endpoint.Port, true, [], endpoint.ExpectedEngine);

    private static DiscoveryProbeResult Closed(DiscoveryEndpoint endpoint) =>
        new(endpoint.Host, endpoint.Port, false, [], endpoint.ExpectedEngine);

    private sealed record ProtocolProbeAttempt(
        bool Connected,
        byte[] Response,
        string? Evidence,
        bool Retryable = false)
    {
        public static readonly ProtocolProbeAttempt ConnectionFailed = new(false, [], null);
    }

    private readonly record struct TdsPacketReadResult(byte[] Response, bool Retryable);
}
