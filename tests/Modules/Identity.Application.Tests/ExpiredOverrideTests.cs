using BuildingBlocks.Messaging;
using Identity.Contracts;
using Identity.Contracts.Permissions;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Identity.Application.Tests;

/// <summary>
/// Aug 2.4: an expired override is not an override. It used to block a new grant or deny on the same
/// permission (409, because the existence check didn't look at expiry), and <c>/effective</c> still listed
/// it. Now the write renews the row in place, in either direction, through the same security-stamp bump
/// as any other write (A7), and the listing leaves expired rows out. Each test plants the expired row
/// with the DbContext, because the validators refuse an expiry in the past.
/// </summary>
[Collection(IdentityApplicationCollection.Name)]
public sealed class ExpiredOverrideTests(IdentityApplicationFixture fixture)
{
    [Fact]
    public async Task A_grant_over_an_expired_deny_renews_it_as_a_grant_and_bumps_the_stamp()
    {
        string target = await CreateTargetAsync();
        await PlantExpiredAsync(UserPermissionOverride.Deny(
            target, IdentityPermissions.UsersRead, IdentityApplicationFixture.CallerId, "old", DateTime.UtcNow.AddDays(-1)));
        string stampBefore = await StampAsync(target);

        Result result = await SendAsync(new GrantPermissionOverrideCommand(target, IdentityPermissions.UsersRead, "new"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        UserPermissionOverride row = await SingleOverrideAsync(target);
        Assert.True(row.IsGranted);
        Assert.Equal("new", row.Reason);
        Assert.Null(row.ExpiresAt);
        Assert.NotEqual(stampBefore, await StampAsync(target));
    }

    [Fact]
    public async Task A_deny_over_an_expired_grant_renews_it_as_a_deny_and_bumps_the_stamp()
    {
        string target = await CreateTargetAsync();
        await PlantExpiredAsync(UserPermissionOverride.Grant(
            target, IdentityPermissions.UsersRead, "someone-else", "old", DateTime.UtcNow.AddDays(-1)));
        string stampBefore = await StampAsync(target);

        Result result = await SendAsync(new DenyPermissionOverrideCommand(target, IdentityPermissions.UsersRead));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        UserPermissionOverride row = await SingleOverrideAsync(target);
        Assert.False(row.IsGranted);
        Assert.Equal(IdentityApplicationFixture.CallerId, row.GrantedById);
        Assert.Null(row.Reason);
        Assert.NotEqual(stampBefore, await StampAsync(target));
    }

    /// <summary>Control: a live override still blocks a second one.</summary>
    [Fact]
    public async Task A_grant_over_a_live_override_is_still_409()
    {
        string target = await CreateTargetAsync();
        await PlantExpiredAsync(UserPermissionOverride.Deny(
            target, IdentityPermissions.UsersRead, IdentityApplicationFixture.CallerId, "live", DateTime.UtcNow.AddDays(1)));

        Result result = await SendAsync(new GrantPermissionOverrideCommand(target, IdentityPermissions.UsersRead));

        Assert.True(result.IsFailure);
        Assert.Equal(409, result.Error.Status);
    }

    [Fact]
    public async Task Effective_permissions_leave_an_expired_override_out()
    {
        string target = await CreateTargetAsync();
        await PlantExpiredAsync(UserPermissionOverride.Grant(
            target, IdentityPermissions.UsersRead, IdentityApplicationFixture.CallerId, "old", DateTime.UtcNow.AddDays(-1)));

        // PF-D1: another user's effective set needs permissions.manage, as through the API.
        await using AsyncServiceScope scope = fixture.CreateScope(new TestCurrentUser(IdentityApplicationFixture.CallerId, IdentityPermissions.PermissionsManage));
        Result<EffectivePermissionsResponse> result = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetUserEffectivePermissionsQuery, EffectivePermissionsResponse>>()
            .HandleAsync(new GetUserEffectivePermissionsQuery(target));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        Assert.Empty(result.Value!.Overrides);
        Assert.DoesNotContain(IdentityPermissions.UsersRead, result.Value.Permissions);
    }

    private async Task<string> CreateTargetAsync()
    {
        string id = Guid.NewGuid().ToString();
        await using AsyncServiceScope scope = fixture.CreateScope();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        IdentityResult created = await userManager.CreateAsync(
            ApplicationUser.Create(id, $"x-{id}@example.com", $"x-{id}@example.com", "X", "Y", UserType.Employee),
            IdentityTestSeeding.Password);
        Assert.True(created.Succeeded);
        return id;
    }

    private async Task PlantExpiredAsync(UserPermissionOverride row)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IdentityModuleDbContext db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();
        db.UserPermissionOverrides.Add(row);
        await db.SaveChangesAsync();
    }

    private async Task<Result> SendAsync<TCommand>(TCommand command)
        where TCommand : ICommand
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(command);
    }

    private async Task<UserPermissionOverride> SingleOverrideAsync(string target)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>()
            .UserPermissionOverrides.AsNoTracking().SingleAsync(o => o.UserId == target);
    }

    private async Task<string> StampAsync(string target)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>()
            .Users.AsNoTracking().SingleAsync(u => u.Id == target)).SecurityStamp!;
    }
}
