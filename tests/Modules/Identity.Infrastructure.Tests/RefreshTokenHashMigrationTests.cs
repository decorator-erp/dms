using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Identity.Infrastructure.Tests;

/// <summary>
/// K5's migration (<c>HashRefreshTokens</c>) against rows that exist before it runs. Every fixture in the
/// repository migrates an empty database, so the in-place hash and the expiry are only ever exercised
/// here. It migrates to <c>InitialIdentity</c>, plants plaintext tokens the way the old code stored them,
/// then runs the rest.
/// </summary>
public sealed class RefreshTokenHashMigrationTests : IAsyncLifetime
{
    private const string MigrationBefore = "20260803151105_InitialIdentity";

    private PostgreSqlContainer _container = null!;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:17").Build();
        await _container.StartAsync();

        await using IdentityModuleDbContext db = CreateContext();
        await db.GetService<IMigrator>().MigrateAsync(MigrationBefore);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task ExistingTokens_AreHashedInPlace_AndExpired()
    {
        await ExecuteAsync("""
            INSERT INTO identity.users
                ("Id", "FirstName", "LastName", "IsActive", "UserType", "CreatedAt",
                 "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES ('legacy-user', 'Legacy', 'User', true, 0, now(), true, false, false, false, 0);
            INSERT INTO identity.refresh_tokens ("Token", "CreatedOn", "ExpiresOn", "RevokedOn", user_id)
            VALUES ('plain-token-one', now(), now() + interval '14 days', NULL, 'legacy-user'),
                   ('plain-token-two', now(), now() + interval '14 days', NULL, 'legacy-user');
            """);

        await using (IdentityModuleDbContext db = CreateContext())
        {
            await db.Database.MigrateAsync();
        }

        await using IdentityModuleDbContext after = CreateContext();
        ApplicationUser user = await after.Users.Include(u => u.RefreshTokens).SingleAsync(u => u.Id == "legacy-user");

        Assert.Equal(2, user.RefreshTokens.Count);
        Assert.Contains(user.RefreshTokens, t => t.TokenHash == RefreshToken.Hash("plain-token-one"));
        Assert.DoesNotContain(user.RefreshTokens, t => t.TokenHash.StartsWith("plain-token", StringComparison.Ordinal));
        Assert.All(user.RefreshTokens, t => Assert.False(t.IsActive));
        Assert.True(user.LockoutEnabled);
    }

    private IdentityModuleDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<IdentityModuleDbContext>().UseNpgsql(_container.GetConnectionString()).Options);

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
