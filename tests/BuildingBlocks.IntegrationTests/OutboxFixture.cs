using BuildingBlocks.Outbox;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace BuildingBlocks.IntegrationTests;

/// <summary>One Postgres container shared across the collection: applies SharedDbContext's migration
/// (owns shared.outbox_messages / shared.inbox_messages) then TestModuleDbContext's (owns
/// test_module.side_effects only — the outbox/inbox tables are excluded from its migration, exactly
/// as a real module would be), in that order — the same module-dependency ordering R6/S3 require in
/// the real system.</summary>
public sealed class OutboxFixture : IAsyncLifetime
{
    private PostgreSqlContainer _container = null!;

    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        await using var shared = new SharedDbContext(
            new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(ConnectionString).Options);
        await shared.Database.MigrateAsync();

        await using var module = new TestModuleDbContext(
            new DbContextOptionsBuilder<TestModuleDbContext>().UseNpgsql(ConnectionString).Options);
        await module.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class OutboxCollection : ICollectionFixture<OutboxFixture>
{
    public const string Name = "Outbox";
}
