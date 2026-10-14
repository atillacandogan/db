using System.Reflection;
using DbScanner.Application;
using DbScanner.Contracts.Scans;
using DbScanner.Domain.Entities;
using Xunit;

namespace DbScanner.Application.Tests;

public sealed class ArchitectureDependencyTests
{
    [Fact]
    public void Domain_DoesNotReferenceOtherDbScannerLayers() =>
        AssertOnlyReferences(typeof(Scan).Assembly);

    [Fact]
    public void Contracts_DoesNotReferenceOtherDbScannerLayers() =>
        AssertOnlyReferences(typeof(ScanDto).Assembly);

    [Fact]
    public void Application_ReferencesOnlyDomainAndContracts() =>
        AssertOnlyReferences(
            typeof(DependencyInjection).Assembly,
            "DbScanner.Domain",
            "DbScanner.Contracts");

    [Fact]
    public void Infrastructure_DoesNotReferenceApiOrPresentationLayers() =>
        AssertOnlyReferences(
            typeof(DbScanner.Infrastructure.DependencyInjection).Assembly,
            "DbScanner.Application",
            "DbScanner.Domain",
            "DbScanner.Contracts");

    private static void AssertOnlyReferences(Assembly assembly, params string[] allowedReferences)
    {
        var allowed = allowedReferences.ToHashSet(StringComparer.Ordinal);
        var forbiddenReferences = assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null
                && name.StartsWith("DbScanner.", StringComparison.Ordinal)
                && !allowed.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            forbiddenReferences.Length == 0,
            $"{assembly.GetName().Name} has forbidden project references: {string.Join(", ", forbiddenReferences)}");
    }
}
