using Xunit;

namespace DbScanner.Infrastructure.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DatabaseContainerTestCollection
{
    public const string Name = "Database container integration";
}
