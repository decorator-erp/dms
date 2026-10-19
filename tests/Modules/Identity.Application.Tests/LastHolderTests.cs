using BuildingBlocks.Messaging;
using Identity.Contracts;
using Identity.Contracts.Permissions;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Identity.Application.Tests;

/// <summary>
/// K6's last-holder rule: a deny of <c>permissions.manage</c> must leave at least one other active user
/// who holds it.
///
/// <para><b>It gets a database of its own</b> (a class fixture, outside the shared collection), because
/// "the last holder" is a statement about every user in the database. In this fresh one the only holder
/// is the fixture's seeded admin caller.</para>
///
/// <para>The caller here is a stale identity: an id that exists only in a token, holding nothing in the
/// database. That is the one realistic way to reach the rule, since the route already requires the
/// caller to hold <c>permissions.manage</c>, and a caller who still holds it is themselves the other
/// holder. Phase 1B's security stamp closes most of that window; this rule is what remains when the
/// window is open.</para>
/// </summary>
public sealed class LastHolderTests(IdentityApplicationFixture fixture) : IClassFixture<IdentityApplicationFixture>
{
    internal static readonly TestCurrentUser StaleCaller = new("stale-token-holder", IdentityPermissions.PermissionsManage);

    [Fact]
    public async Task DenyingPermissionsManage_ToTheLastHolder_Is409_AndToOneOfTwo_Succeeds()
    {
        await using (AsyncServiceScope scope = fixture.CreateScope(StaleCaller))
        {
            Result lastHolder = await DenyAsync(scope.ServiceProvider, IdentityApplicationFixture.CallerId);

            Assert.True(lastHolder.IsFailure);
            Assert.Equal("permissions.LastHolder", lastHolder.Error.Code);
            Assert.Equal(409, lastHolder.Error.Status);
        }

        // A second active admin makes the first one no longer the last.
        await using (AsyncServiceScope scope = fixture.CreateScope(StaleCaller))
        {
            UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var second = ApplicationUser.Create(
                Guid.NewGuid().ToString(), $"second-admin-{Guid.NewGuid():N}@example.com",
                $"second-admin-{Guid.NewGuid():N}@example.com", "Second", "Admin", UserType.Employee);
            await userManager.CreateAsync(second, IdentityTestSeeding.Password);
            await userManager.AddToRoleAsync(second, "admin");

            Result oneOfTwo = await DenyAsync(scope.ServiceProvider, IdentityApplicationFixture.CallerId);

            Assert.True(oneOfTwo.IsSuccess, oneOfTwo.IsFailure ? oneOfTwo.Error.ToString() : null);
        }
    }

    internal static Task<Result> DenyAsync(IServiceProvider sp, string targetUserId) =>
        sp.GetRequiredService<ICommandHandler<DenyPermissionOverrideCommand>>()
            .HandleAsync(new DenyPermissionOverrideCommand(targetUserId, IdentityPermissions.PermissionsManage));
}

/// <summary>K6's last-holder rule weighs <b>active</b> holders: a deactivated account cannot repair
/// permissions. Its own database, for the same reason as <see cref="LastHolderTests"/>.</summary>
public sealed class LastHolderInactiveTests(IdentityApplicationFixture fixture) : IClassFixture<IdentityApplicationFixture>
{
    [Fact]
    public async Task AnInactiveHolder_DoesNotCountAsTheOtherHolder()
    {
        string inactiveId = Guid.NewGuid().ToString();
        string targetId = Guid.NewGuid().ToString();

        await using AsyncServiceScope scope = fixture.CreateScope(LastHolderTests.StaleCaller);
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        RoleManager<ApplicationRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

        // A role of their own, so this test doesn't depend on the admin role's membership.
        var role = ApplicationRole.Create(Guid.NewGuid().ToString(), $"managers-{Guid.NewGuid():N}");
        await roleManager.CreateAsync(role);
        IdentityModuleDbContext db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();
        db.RoleClaims.Add(ApplicationRoleClaim.Create(
            role.Id, BuildingBlocks.PermissionClaim.Type, IdentityPermissions.PermissionsManage));
        await db.SaveChangesAsync();

        var target = ApplicationUser.Create(
            targetId, $"t-{targetId}@example.com", $"t-{targetId}@example.com", "T", "U", UserType.Employee);
        var inactive = ApplicationUser.Create(
            inactiveId, $"i-{inactiveId}@example.com", $"i-{inactiveId}@example.com", "I", "U", UserType.Employee,
            isActive: false);
        await userManager.CreateAsync(target, IdentityTestSeeding.Password);
        await userManager.CreateAsync(inactive, IdentityTestSeeding.Password);
        await userManager.AddToRoleAsync(target, role.Name!);
        await userManager.AddToRoleAsync(inactive, role.Name!);

        // Deny the seeded admin first, so the role above holds the only remaining holders. This one is
        // allowed: the target is still an active holder.
        Result first = await LastHolderTests.DenyAsync(scope.ServiceProvider, IdentityApplicationFixture.CallerId);
        Result second = await LastHolderTests.DenyAsync(scope.ServiceProvider, targetId);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error.ToString() : null);
        Assert.True(second.IsFailure);
        Assert.Equal("permissions.LastHolder", second.Error.Code);
    }
}
