using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Identity.Infrastructure.Tests;

/// <summary>G1-04 verify: unit tests for permission resolution including the override path (grant and
/// revoke/deny).</summary>
[Collection(IdentityInfrastructureCollection.Name)]
public sealed class EffectivePermissionServiceTests(IdentityInfrastructureFixture fixture)
{
    [Fact]
    public async Task ResolveAsync_ReturnsRoleGrantedPermissions()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        (ApplicationUser? user, ApplicationRole _) = await TestDataBuilder.CreateUserInRoleAsync(scope.ServiceProvider, "employees.read", "departments.read");

        IEnumerable<string> resolved = await scope.ServiceProvider.GetRequiredService<IEffectivePermissionService>()
            .ResolveAsync(user.Id);

        Assert.Equal(["employees.read", "departments.read"], resolved.ToHashSet());
    }

    [Fact]
    public async Task ResolveAsync_GrantOverride_AddsAPermissionNotOnTheRole()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        (ApplicationUser? user, ApplicationRole _) = await TestDataBuilder.CreateUserInRoleAsync(scope.ServiceProvider, "employees.read");
        IdentityModuleDbContext db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();

        db.UserPermissionOverrides.Add(UserPermissionOverride.Grant(user.Id, "roles.manage", "admin-1"));
        await db.SaveChangesAsync();

        IEnumerable<string> resolved = await scope.ServiceProvider.GetRequiredService<IEffectivePermissionService>()
            .ResolveAsync(user.Id);

        Assert.Contains("roles.manage", resolved);
        Assert.Contains("employees.read", resolved);
    }

    [Fact]
    public async Task ResolveAsync_DenyOverride_RemovesAPermissionTheRoleGrants()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        (ApplicationUser? user, ApplicationRole _) = await TestDataBuilder.CreateUserInRoleAsync(
            scope.ServiceProvider, "employees.read", "employees.delete");
        IdentityModuleDbContext db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();

        db.UserPermissionOverrides.Add(UserPermissionOverride.Deny(user.Id, "employees.delete", "admin-1"));
        await db.SaveChangesAsync();

        IEnumerable<string> resolved = await scope.ServiceProvider.GetRequiredService<IEffectivePermissionService>()
            .ResolveAsync(user.Id);

        Assert.Contains("employees.read", resolved);
        Assert.DoesNotContain("employees.delete", resolved);
    }

    [Fact]
    public async Task ResolveAsync_ExpiredOverride_DoesNotApply()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        (ApplicationUser? user, ApplicationRole _) = await TestDataBuilder.CreateUserInRoleAsync(scope.ServiceProvider, "employees.read");
        IdentityModuleDbContext db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();

        db.UserPermissionOverrides.Add(
            UserPermissionOverride.Grant(user.Id, "roles.manage", "admin-1", expiresAt: DateTime.UtcNow.AddDays(-1)));
        await db.SaveChangesAsync();

        IEnumerable<string> resolved = await scope.ServiceProvider.GetRequiredService<IEffectivePermissionService>()
            .ResolveAsync(user.Id);

        // PC-03 liveness: the user's role grants employees.read, so an empty resolve would satisfy the
        // absence assertion below while proving nothing about the override having expired.
        Assert.Contains("employees.read", resolved);
        Assert.DoesNotContain("roles.manage", resolved);
    }

    [Fact]
    public async Task ResolveAsync_UnknownUser_ReturnsEmpty()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();

        IEnumerable<string> resolved = await scope.ServiceProvider.GetRequiredService<IEffectivePermissionService>()
            .ResolveAsync(Guid.NewGuid().ToString());

        Assert.Empty(resolved);
    }
}
