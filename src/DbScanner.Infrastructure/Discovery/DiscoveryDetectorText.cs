using System.Text;

namespace DbScanner.Infrastructure.Discovery;

internal static class DiscoveryDetectorText
{
    public static string Banner(byte[] bytes) => Encoding.ASCII.GetString(bytes);
}
