using DbScanner.Infrastructure.Discovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

public sealed class DiscoveryOptionsValidationTests
{
    [Fact]
    public void ValidConfiguration_PassesValidationAndReplacesDefaultPortArrays()
    {
        var options = Resolve(new Dictionary<string, string?>
        {
            ["Discovery:ConnectTimeoutMilliseconds"] = "1200",
            ["Discovery:BannerReadTimeoutMilliseconds"] = "250",
            ["Discovery:DnsTimeoutMilliseconds"] = "1800",
            ["Discovery:JobTimeoutSeconds"] = "240",
            ["Discovery:MaxConcurrency"] = "6",
            ["Discovery:MaxProbesPerSecond"] = "12",
            ["Discovery:ProbeDelayMilliseconds"] = "75",
            ["Discovery:MaxCidrHosts"] = "128",
            ["Discovery:MaxPortsPerTarget"] = "12",
            ["Discovery:MaxBannerBytes"] = "1024",
            ["Discovery:LocalNetworkPorts:mysql:0"] = "3306",
            ["Discovery:LocalNetworkPorts:mysql:1"] = "13306",
            ["Discovery:LocalNetworkPorts:postgresql:0"] = "5432",
            ["Discovery:LocalNetworkPorts:mssql:0"] = "1433",
            ["Discovery:LocalNetworkPorts:oracle:0"] = "1521",
            ["Discovery:UnsupportedDatabasePorts:0"] = "6380"
        });

        Assert.Equal(6, options.MaxConcurrency);
        Assert.Equal(1024, options.MaxBannerBytes);
        Assert.Equal([3306, 13306], options.LocalNetworkPorts["mysql"]);
        Assert.Equal([6380], options.UnsupportedDatabasePorts);
    }

    [Fact]
    public void MissingDiscoverySection_UsesSafeDefaults()
    {
        var options = Resolve([]);

        Assert.Equal(1000, options.ConnectTimeoutMilliseconds);
        Assert.Equal(300, options.JobTimeoutSeconds);
        Assert.Equal(8, options.MaxConcurrency);
        Assert.Equal(256, options.MaxCidrHosts);
        Assert.Equal(16, options.MaxPortsPerTarget);
        Assert.Equal(512, options.MaxBannerBytes);
        Assert.Equal([3306, 13306], options.LocalNetworkPorts["mysql"]);
        Assert.Equal([6379, 27017], options.UnsupportedDatabasePorts);
        Assert.True(DiscoveryOptions.IsValid(options));
    }

    [Theory]
    [InlineData("Discovery:MaxConcurrency", "0", "Discovery:MaxConcurrency must be between 1 and 32. Current value: 0.")]
    [InlineData("Discovery:MaxConcurrency", "33", "Discovery:MaxConcurrency must be between 1 and 32. Current value: 33.")]
    [InlineData("Discovery:JobTimeoutSeconds", "9", "Discovery:JobTimeoutSeconds must be between 10 and 3600. Current value: 9.")]
    [InlineData("Discovery:MaxCidrHosts", "4097", "Discovery:MaxCidrHosts must be between 1 and 4096. Current value: 4097.")]
    [InlineData("Discovery:MaxBannerBytes", "63", "Discovery:MaxBannerBytes must be between 64 and 4096. Current value: 63.")]
    [InlineData("Discovery:MaxPortsPerTarget", "0", "Discovery:MaxPortsPerTarget must be between 1 and 64. Current value: 0.")]
    public void InvalidConfiguration_ReportsPropertyNameAndValue(
        string key,
        string value,
        string expectedFailure)
    {
        var exception = Assert.Throws<OptionsValidationException>(() => Resolve(new Dictionary<string, string?>
        {
            [key] = value
        }));

        Assert.Contains(expectedFailure, exception.Failures);
    }

    [Fact]
    public void DuplicateConfiguredPort_ReportsEngineAndValues()
    {
        var exception = Assert.Throws<OptionsValidationException>(() => Resolve(new Dictionary<string, string?>
        {
            ["Discovery:LocalNetworkPorts:mysql:0"] = "3306",
            ["Discovery:LocalNetworkPorts:mysql:1"] = "3306"
        }));

        Assert.Contains(
            "Discovery:LocalNetworkPorts:mysql cannot contain duplicate port values. Current values: 3306, 3306.",
            exception.Failures);
    }

    [Fact]
    public void UnsupportedPortOverlappingSupportedPort_IsRejected()
    {
        var exception = Assert.Throws<OptionsValidationException>(() => Resolve(new Dictionary<string, string?>
        {
            ["Discovery:UnsupportedDatabasePorts:0"] = "3306"
        }));

        Assert.Contains(
            "Discovery:UnsupportedDatabasePorts cannot overlap supported engine ports. Overlapping values: 3306.",
            exception.Failures);
    }

    private static DiscoveryOptions Resolve(IEnumerable<KeyValuePair<string, string?>> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:dbscanner"] = "Host=localhost;Database=test;Username=test"
            })
            .AddInMemoryCollection(values)
            .Build();
        using var provider = new ServiceCollection()
            .AddDbScannerInfrastructure(configuration)
            .BuildServiceProvider();
        return provider.GetRequiredService<IOptions<DiscoveryOptions>>().Value;
    }
}
