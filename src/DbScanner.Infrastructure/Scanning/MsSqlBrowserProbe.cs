using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using DbScanner.Application.Models;
using DbScanner.Domain.Entities;
using DbScanner.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DbScanner.Infrastructure.Scanning;

internal sealed class MsSqlBrowserProbe(
    ILogger<MsSqlBrowserProbe> logger) : IMsSqlBrowserProbe
{
    private const int BrowserPort = 1434;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    public async Task<ScanScriptExecutionResult> RunAsync(
        Target target,
        NseScript script,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(target.Host, cancellationToken);
            var address = addresses.FirstOrDefault(item =>
                item.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6);
            if (address is null)
            {
                return CreateResult(
                    script,
                    ProbeResultStatus.ConnectionFailed,
                    "host_unreachable",
                    exitCode: 1);
            }

            using var udpClient = new UdpClient(address.AddressFamily);
            var endpoint = new IPEndPoint(address, BrowserPort);
            await udpClient.SendAsync([0x02], 1, endpoint);

            var receiveTask = udpClient.ReceiveAsync(cancellationToken).AsTask();
            var completedTask = await Task.WhenAny(
                receiveTask,
                Task.Delay(Timeout, cancellationToken));
            if (completedTask != receiveTask)
            {
                return CreateResult(
                    script,
                    ProbeResultStatus.Passed,
                    "udp_1434_no_response",
                    exitCode: 0);
            }

            var response = await receiveTask;
            var payload = response.Buffer.Length > 3
                ? Encoding.ASCII.GetString(response.Buffer.AsSpan(3))
                : string.Empty;
            var instanceCount = payload.Split("ServerName;", StringSplitOptions.RemoveEmptyEntries).Length;
            var evidence = $"udp_1434_response_received,instances={Math.Max(1, instanceCount)}";

            return CreateResult(script, ProbeResultStatus.Failed, evidence, exitCode: 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is SocketException
                or InvalidOperationException
                or TimeoutException)
        {
            logger.LogWarning(
                "Microsoft SQL Server Browser discovery failed for target {TargetId} with category {Category}.",
                target.Id,
                Classify(exception));

            return CreateResult(
                script,
                ProbeResultStatus.ConnectionFailed,
                Classify(exception),
                exitCode: 1);
        }
    }

    private static ScanScriptExecutionResult CreateResult(
        NseScript script,
        ProbeResultStatus status,
        string evidence,
        int exitCode)
    {
        var output = string.Join(
            "; ",
            $"check=mssql_browser_exposure",
            $"status={ToToken(status)}",
            $"evidence={Normalize(evidence)}");
        var rawXml = new XElement(
            "script",
            new XAttribute("id", script.NseScriptName),
            new XAttribute("runner", "dbscanner-mssql-browser"),
            new XAttribute("output", output))
            .ToString(SaveOptions.DisableFormatting);

        return new ScanScriptExecutionResult(
            script.Id,
            script.NseScriptName,
            rawXml,
            output,
            exitCode,
            status);
    }

    private static string Classify(Exception exception) => exception switch
    {
        SocketException socketException when socketException.SocketErrorCode == SocketError.HostNotFound =>
            "host_unreachable",
        SocketException => "udp_probe_error",
        TimeoutException => "connection_timeout",
        _ => "unexpected_probe_error"
    };

    private static string ToToken(ProbeResultStatus status) =>
        status.ToString().ToLowerInvariant();

    private static string Normalize(string value) =>
        value.Replace(';', '_').Replace('=', '_').Replace('\r', ' ').Replace('\n', ' ').Trim();
}
