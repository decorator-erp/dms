using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Infrastructure.Tests;

/// <summary>Creates a unique user + role (+ optional permission claims on the role) per call, so tests
/// sharing one container/schema never collide.</summary>
internal static class TestDataBuilder
{
    public static async Task<(ApplicationUser User, ApplicationRole Role)> CreateUserInRoleAsync(
        IServiceProvider services, params string[] rolePermissions)
    {
        string suffix = Guid.NewGuid().ToString("N");

        UserManager<ApplicationUser> userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        RoleManager<ApplicationRole> roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        IdentityModuleDbContext db = services.GetRequiredService<IdentityModuleDbContext>();

        var user = ApplicationUser.Create(
            Guid.NewGuid().ToString(), $"user-{suffix}", $"user-{suffix}@example.com", "Test", "User", UserType.Employee);
        IdentityResult createResult = await userManager.CreateAsync(user, "P@ssw0rd123!");
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", createResult.Errors.Select(e => e.Description)));
        }

        var role = ApplicationRole.Create(Guid.NewGuid().ToString(), $"role-{suffix}");
        IdentityResult roleResult = await roleManager.CreateAsync(role);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", roleResult.Errors.Select(e => e.Description)));
        }

        foreach (string permission in rolePermissions)
        {
            db.RoleClaims.Add(ApplicationRoleClaim.Create(role.Id, BuildingBlocks.PermissionClaim.Type, permission));
        }

        if (rolePermissions.Length > 0)
        {
            await db.SaveChangesAsync();
        }

        await userManager.AddToRoleAsync(user, role.Name!);

        return (user, role);
    }
}
