using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Persistence.Configurations;
using Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Identity.Infrastructure.Tests;

/// <summary>
/// Aug 4.2: <see cref="EffectivePermissionService"/> answers in one statement, with the same semantics.
/// The counts use a context of the test's own with a <c>CommandExecuted</c> counter (the
/// <c>HrDirectoryTests</c> pattern). Every test uses a permission code of its own, so the shared database
/// can't leak another test's holders in.
/// </summary>
[Collection(IdentityInfrastructureCollection.Name)]
public sealed class Phase4PermissionQueryTests(IdentityInfrastructureFixture fixture)
{
    [Fact]
    public async Task ResolveAsync_is_one_statement()
    {
        string code = NewCode();
        (ApplicationUser user, _) = await CreateUserInRoleAsync(code, NewCode());
        await AddOverrideAsync(UserPermissionOverride.Grant(user.Id, NewCode(), "admin-1"));

        int commands = 0;
        await using IdentityModuleDbContext db = CountingContext(() => commands++);
        IEnumerable<string> resolved = await new EffectivePermissionService(db).ResolveAsync(user.Id);

        Assert.Contains(code, resolved);
        Assert.Equal(1, commands);
    }

    [Fact]
    public async Task ResolveAsync_semantics_role_minus_live_deny_plus_grant_and_expired_overrides_ignored()
    {
        string denied = NewCode();
        string kept = NewCode();
        string granted = NewCode();
        string expiredGrant = NewCode();
        (ApplicationUser user, _) = await CreateUserInRoleAsync(denied, kept);
        await AddOverrideAsync(UserPermissionOverride.Deny(user.Id, denied, "admin-1"));
        await AddOverrideAsync(UserPermissionOverride.Grant(user.Id, granted, "admin-1"));
        await AddOverrideAsync(UserPermissionOverride.Deny(user.Id, kept, "admin-1", expiresAt: DateTime.UtcNow.AddDays(-1)));
        await AddOverrideAsync(UserPermissionOverride.Grant(user.Id, expiredGrant, "admin-1", expiresAt: DateTime.UtcNow.AddDays(-1)));

        await using AsyncServiceScope scope = fixture.CreateScope();
        HashSet<string> resolved = [.. await scope.ServiceProvider.GetRequiredService<IEffectivePermissionService>().ResolveAsync(user.Id)];

        Assert.DoesNotContain(denied, resolved);
        Assert.Contains(kept, resolved); // its deny expired
        Assert.Contains(granted, resolved); // no role, but a live grant
        Assert.DoesNotContain(expiredGrant, resolved);
    }

    [Fact]
    public async Task ResolveAsync_for_an_unknown_user_is_empty()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();

        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IEffectivePermissionService>()
            .ResolveAsync(Guid.NewGuid().ToString()));
    }

    [Fact]
    public async Task HasOtherActiveHolderAsync_is_one_statement_however_many_candidates()
    {
        string code = NewCode();
        (ApplicationUser excluded, ApplicationRole role) = await CreateUserInRoleAsync(code);
        for (int i = 0; i < 5; i++)
        {
            ApplicationUser other = await CreateUserAsync();
            await AddToRoleAsync(other, role);
        }

        int commands = 0;
        await using IdentityModuleDbContext db = CountingContext(() => commands++);
        bool other5 = await new EffectivePermissionService(db).HasOtherActiveHolderAsync(code, excluded.Id);

        Assert.True(other5);
        Assert.Equal(1, commands);
    }

    [Fact]
    public async Task A_holder_only_through_an_expired_grant_is_not_a_holder()
    {
        string code = NewCode();
        ApplicationUser user = await CreateUserAsync();
        await AddOverrideAsync(UserPermissionOverride.Grant(user.Id, code, "admin-1", expiresAt: DateTime.UtcNow.AddDays(-1)));

        Assert.False(await HasOtherAsync(code));
    }

    [Fact]
    public async Task A_role_holder_with_a_live_deny_is_not_a_holder()
    {
        string code = NewCode();
        (ApplicationUser user, _) = await CreateUserInRoleAsync(code);
        await AddOverrideAsync(UserPermissionOverride.Deny(user.Id, code, "admin-1"));

        Assert.False(await HasOtherAsync(code));
    }

    [Fact]
    public async Task A_live_grant_without_a_role_is_a_holder_and_an_inactive_one_is_not()
    {
        string code = NewCode();
        ApplicationUser active = await CreateUserAsync();
        await AddOverrideAsync(UserPermissionOverride.Grant(active.Id, code, "admin-1"));
        string inactiveCode = NewCode();
        ApplicationUser inactive = await CreateUserAsync(isActive: false);
        await AddOverrideAsync(UserPermissionOverride.Grant(inactive.Id, inactiveCode, "admin-1"));

        Assert.True(await HasOtherAsync(code));
        Assert.False(await HasOtherAsync(inactiveCode));
    }

    /// <summary>
    /// P5-F2: a live grant and a live deny for the same user and code is a holder for neither the resolver
    /// nor the last-holder check: deny wins in both. The unique (UserId, Permission) index forbids that
    /// pair, so the test drops it inside a transaction (Postgres DDL is transactional) and rolls back,
    /// which restores the index and removes the rows for every other test.
    /// </summary>
    [Fact]
    public async Task A_live_grant_and_a_live_deny_for_the_same_code_is_not_a_holder_and_the_resolver_agrees()
    {
        string code = NewCode();
        ApplicationUser other = await CreateUserAsync();

        int commands = 0;
        await using IdentityModuleDbContext db = CountingContext(() => commands++);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync();
        try
        {
            string dropIndex = "DROP INDEX identity.\"" + UserPermissionOverrideConfiguration.UniqueUserPermissionIndex + "\"";
            await db.Database.ExecuteSqlRawAsync(dropIndex);
            db.UserPermissionOverrides.Add(UserPermissionOverride.Grant(other.Id, code, "admin-1"));
            db.UserPermissionOverrides.Add(UserPermissionOverride.Deny(other.Id, code, "admin-1"));
            await db.SaveChangesAsync();

            var service = new EffectivePermissionService(db);
            commands = 0;
            bool holder = await service.HasOtherActiveHolderAsync(code, Guid.NewGuid().ToString());

            Assert.False(holder);
            Assert.Equal(1, commands); // still one EXISTS statement
            Assert.DoesNotContain(code, await service.ResolveAsync(other.Id));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task The_excluded_user_does_not_count_for_themselves()
    {
        string code = NewCode();
        (ApplicationUser user, _) = await CreateUserInRoleAsync(code);

        await using AsyncServiceScope scope = fixture.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<IEffectivePermissionService>()
            .HasOtherActiveHolderAsync(code, user.Id));
    }

    private async Task<bool> HasOtherAsync(string code)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IEffectivePermissionService>()
            .HasOtherActiveHolderAsync(code, Guid.NewGuid().ToString());
    }

    private async Task<(ApplicationUser, ApplicationRole)> CreateUserInRoleAsync(params string[] codes)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await TestDataBuilder.CreateUserInRoleAsync(scope.ServiceProvider, codes);
    }

    private async Task<ApplicationUser> CreateUserAsync(bool isActive = true)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        string id = Guid.NewGuid().ToString();
        var user = ApplicationUser.Create(id, $"p4-{id}", $"p4-{id}@example.com", "P", "Four", UserType.Employee, isActive: isActive);
        Assert.True((await scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>()
            .CreateAsync(user, "P@ssw0rd123!")).Succeeded);
        return user;
    }

    private async Task AddToRoleAsync(ApplicationUser user, ApplicationRole role)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IdentityModuleDbContext db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();
        db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id });
        await db.SaveChangesAsync();
    }

    private async Task AddOverrideAsync(UserPermissionOverride row)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IdentityModuleDbContext db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();
        db.UserPermissionOverrides.Add(row);
        await db.SaveChangesAsync();
    }

    private IdentityModuleDbContext CountingContext(Action onCommand) =>
        new(new DbContextOptionsBuilder<IdentityModuleDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .LogTo(_ => onCommand(), [RelationalEventId.CommandExecuted], LogLevel.Information)
            .Options);

    private static string NewCode() => $"p4.{Guid.NewGuid():N}";
}
