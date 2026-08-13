using System.Net;

namespace DbScanner.Domain.Services;

public static class TargetEndpointNormalizer
{
    public static string NormalizeHost(string host)
    {
        var trimmed = host.Trim().TrimEnd('.');
        return IPAddress.TryParse(trimmed, out var address)
            ? address.ToString().ToLowerInvariant()
            : trimmed.ToLowerInvariant();
    }
}
