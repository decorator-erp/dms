using Xunit;

namespace Integration.Tests;

/// <summary>Shares one <see cref="ApiFixture"/> (one container, one migrated database) across every
/// test class in this project — each is expensive to spin up, and nothing here needs isolation from
/// the others at that granularity.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "Api";
}
