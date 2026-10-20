using BuildingBlocks.Messaging;
using Identity.Contracts;
using Identity.Contracts.Permissions;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel;
using Xunit;

namespace Identity.Application.Tests;

/// <summary>
/// Phase 5 (5.2, H6): an override whose own <c>IsActive</c> is false counts for nothing, at every site
/// that reads overrides. Nothing in the app sets the flag false yet (revoke is a hard delete), so each
/// test plants the row and then clears the flag with SQL, the way a future soft revoke would leave it.
/// </summary>
[Collection(IdentityApplicationCollection.Name)]
public sealed class InactiveOverrideTests(IdentityApplicationFixture fixture)
{
    [Fact]
    public async Task An_inactive_grant_confers_nothing_and_does_not_count_as_another_holder()
    {
        string target = await CreateTargetAsync();
        string code = $"phase5.inactive.{Guid.NewGuid():N}";
        await PlantInactiveAsync(UserPermissionOverride.Grant(target, code, IdentityApplicationFixture.CallerId));

        await using AsyncServiceScope scope = fixture.CreateScope();
        IEffectivePermissionService service = scope.ServiceProvider.GetRequiredService<IEffectivePermissionService>();

        Assert.DoesNotContain(code, await service.ResolveAsync(target));
        Assert.False(await service.HasOtherActiveHolderAsync(code, excludingUserId: "nobody"));
    }

    /// <summary>Control for the test above: the same row, left active, does both.</summary>
    [Fact]
    public async Task The_same_grant_left_active_confers_the_code_and_counts_as_a_holder()
    {
        string target = await CreateTargetAsync();
        string code = $"phase5.active.{Guid.NewGuid():N}";
        await PlantAsync(UserPermissionOverride.Grant(target, code, IdentityApplicationFixture.CallerId), deactivate: false);

        await using AsyncServiceScope scope = fixture.CreateScope();
        IEffectivePermissionService service = scope.ServiceProvider.GetRequiredService<IEffectivePermissionService>();

        Assert.Contains(code, await service.ResolveAsync(target));
        Assert.True(await service.HasOtherActiveHolderAsync(code, excludingUserId: "nobody"));
    }

    [Fact]
    public async Task Effective_permissions_leave_an_inactive_override_out()
    {
        string target = await CreateTargetAsync();
        await PlantInactiveAsync(UserPermissionOverride.Grant(target, IdentityPermissions.UsersRead, IdentityApplicationFixture.CallerId));

        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<EffectivePermissionsResponse> result = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetUserEffectivePermissionsQuery, EffectivePermissionsResponse>>()
            .HandleAsync(new GetUserEffectivePermissionsQuery(target));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        Assert.Empty(result.Value!.Overrides);
        Assert.DoesNotContain(IdentityPermissions.UsersRead, result.Value.Permissions);
    }

    /// <summary>The unique (UserId, Permission) index covers the inactive row, so a new grant can only
    /// succeed by renewing it in place: no 409, and the row is active again.</summary>
    [Fact]
    public async Task A_grant_over_an_inactive_override_renews_it_and_reactivates_it()
    {
        string target = await CreateTargetAsync();
        await PlantInactiveAsync(UserPermissionOverride.Deny(target, IdentityPermissions.UsersRead, IdentityApplicationFixture.CallerId, "old"));

        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            Result result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<GrantPermissionOverrideCommand>>()
                .HandleAsync(new GrantPermissionOverrideCommand(target, IdentityPermissions.UsersRead, "new"));
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        }

        await using AsyncServiceScope verify = fixture.CreateScope();
        UserPermissionOverride row = await verify.ServiceProvider.GetRequiredService<IdentityModuleDbContext>()
            .UserPermissionOverrides.AsNoTracking().SingleAsync(o => o.UserId == target);
        Assert.True(row.IsActive);
        Assert.True(row.IsGranted);
        Assert.Equal("new", row.Reason);
        Assert.Contains(IdentityPermissions.UsersRead,
            await verify.ServiceProvider.GetRequiredService<IEffectivePermissionService>().ResolveAsync(target));
    }

    private async Task<string> CreateTargetAsync()
    {
        string id = Guid.NewGuid().ToString();
        await using AsyncServiceScope scope = fixture.CreateScope();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        IdentityResult created = await userManager.CreateAsync(
            ApplicationUser.Create(id, $"i-{id}@example.com", $"i-{id}@example.com", "I", "N", UserType.Employee),
            IdentityTestSeeding.Password);
        Assert.True(created.Succeeded);
        return id;
    }

    private Task PlantInactiveAsync(UserPermissionOverride row) => PlantAsync(row, deactivate: true);

    private async Task PlantAsync(UserPermissionOverride row, bool deactivate)
    {
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            IdentityModuleDbContext db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();
            db.UserPermissionOverrides.Add(row);
            await db.SaveChangesAsync();
        }

        if (deactivate)
        {
            await using var connection = new NpgsqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                """UPDATE identity.user_permission_overrides SET "IsActive" = false WHERE "Id" = @id""", connection);
            command.Parameters.AddWithValue("id", row.Id);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }
    }
}
