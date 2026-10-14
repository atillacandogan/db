using DbScanner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DbScanner.Infrastructure.Tests;

public sealed class InfrastructureConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Missing_connection_is_rejected_instead_of_using_a_shared_password(string? connection)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connection
        }).Build();
        var error = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddDbScannerInfrastructure(configuration));
        Assert.Contains("No default database credential", error.Message);
    }

    [Theory]
    [InlineData("dbscanner")]
    [InlineData("DefaultConnection")]
    public void An_explicit_connection_is_used_without_contacting_a_database(string name)
    {
        const string connection = "Host=localhost;Database=configuration-test;Username=test";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{name}"] = connection
        }).Build();
        using var services = new ServiceCollection().AddDbScannerInfrastructure(configuration).BuildServiceProvider();
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DbScannerDbContext>();
        Assert.Equal(connection, db.Database.GetDbConnection().ConnectionString);
    }
}
