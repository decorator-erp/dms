using Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Identity.Infrastructure.Persistence.Seeders;

/// <summary>
/// Ported from <c>../backend/Infrastructure/Persistence/Seeders/UserSeeder.cs</c>, with two changes.
/// R7 fix: the backend seeds a single fixed user with a hardcoded pre-hashed password. Shipping a known
/// password hash in source is a credential in the repository. Instead the password comes from
/// configuration key <c>DMS_SEED_ADMIN_PASSWORD</c> (settable via user-secrets for local dev, or an
/// actual environment variable of that name elsewhere — <see cref="IConfiguration"/>'s environment
/// variable provider exposes a flat env var under its own name with no prefix needed). Outside
/// <c>Development</c>, an unset value fails startup rather than silently skipping the seed or falling
/// back to a default. Second: the backend assigns its seeded user to the <c>Manager</c> role despite
/// calling it "the admin user" (a naming quirk, not a deliberate choice) — this one joins
/// <see cref="DefaultRoles.Admin"/> instead, matching what it's actually called.
/// </summary>
internal static class AdminUserSeeder
{
    /// <summary>Fixed id (backend's own idempotency key — lookup by id, not email) so re-running finds
    /// the same row it created last time even if email/username were ever renamed.</summary>
    private const string SeedAdminUserId = "00000000-0000-0000-0000-000000000001";
    private const string SeedAdminEmail = "admin@system.dms";

    public static async Task SeedAsync(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        IConfiguration configuration,
        IHostEnvironment environment,
        CancellationToken ct = default)
    {
        if (await userManager.FindByIdAsync(SeedAdminUserId) is not null)
            return;

        var password = configuration["DMS_SEED_ADMIN_PASSWORD"];
        if (string.IsNullOrWhiteSpace(password))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "DMS_SEED_ADMIN_PASSWORD is not configured. Set it as an environment variable, or "
                    + "via `dotnet user-secrets set \"DMS_SEED_ADMIN_PASSWORD\" \"<a strong password>\" "
                    + "--project src/Api` for local development. There is no default — a committed seed "
                    + "password is a credential in the repository.");
            }

            return;
        }

        var role = await roleManager.FindByNameAsync(DefaultRoles.Admin)
            ?? throw new InvalidOperationException(
                $"Cannot seed the admin user before role '{DefaultRoles.Admin}' exists — RoleSeeder must run first.");

        var user = ApplicationUser.Create(
            SeedAdminUserId, SeedAdminEmail, SeedAdminEmail, "System", "Administrator", UserType.Employee);

        var createResult = await userManager.CreateAsync(user, password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                "Failed to seed the admin user: "
                + string.Join("; ", createResult.Errors.Select(e => e.Description)));
        }

        await userManager.AddToRoleAsync(user, role.Name!);
    }
}
