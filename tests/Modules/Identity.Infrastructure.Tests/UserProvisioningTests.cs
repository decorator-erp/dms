using Identity.Contracts;
using Identity.Domain;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Persistence.Seeders;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Identity.Infrastructure.Tests;

/// <summary>
/// <c>UserProvisioning</c> is what replaced self-registration, and the two properties that made the
/// replacement worth doing are asserted here: a provisioned user cannot log in yet, and cannot be
/// provisioned into a full-access role.
/// </summary>
[Collection(IdentityInfrastructureCollection.Name)]
public sealed class UserProvisioningTests(IdentityInfrastructureFixture fixture)
{
    private const string ValidPassword = "P@ssw0rd123!";

    [Fact]
    public async Task Provisioned_user_is_created_inactive()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        await EnsureRolesAsync(scope.ServiceProvider);

        Result<string> result = await ProvisionAsync(scope.ServiceProvider, UserKind.Employee, IdentityRoles.Employee);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);

        ApplicationUser user = await LoadAsync(scope.ServiceProvider, result.Value!);
        Assert.False(user.IsActive);
    }

    /// <summary>
    /// The escalation path self-registration left open. Asserted against
    /// <c>DefaultRoles.FullAccessRoles</c> rather than the literals "admin"/"manager", so a role promoted
    /// into that array later is covered the day it is added.
    ///
    /// <para>Two things make this non-vacuous. The roles are seeded first, so a rejection cannot be a
    /// <c>RoleErrors.NotFound</c> wearing the wrong name; and the exact error code is asserted, not
    /// merely <c>IsFailure</c>. The positive control below it proves the same request shape succeeds
    /// when only the role name changes.</para>
    /// </summary>
    [Fact]
    public async Task Full_access_roles_are_refused()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        await EnsureRolesAsync(scope.ServiceProvider);

        Assert.NotEmpty(DefaultRoles.FullAccessRoles);

        foreach (string privilegedRole in DefaultRoles.FullAccessRoles)
        {
            Result<string> result = await ProvisionAsync(scope.ServiceProvider, UserKind.Employee, privilegedRole);

            Assert.True(result.IsFailure, $"Provisioning into '{privilegedRole}' succeeded. It must not be reachable.");
            Assert.Equal(UserErrors.RoleNotAssignableAtProvisioning.Code, result.Error.Code);
        }

        // POSITIVE CONTROL — same call, unprivileged role, so the loop above cannot be passing because
        // provisioning is broken outright.
        Result<string> control = await ProvisionAsync(scope.ServiceProvider, UserKind.Employee, IdentityRoles.Employee);
        Assert.True(control.IsSuccess, control.IsFailure ? control.Error.ToString() : null);
    }

    /// <summary>The granted role is the one asked for, and only that one. Paired with the refusal test
    /// above: together they say a caller gets exactly the unprivileged role it named and cannot widen
    /// that. HR and CRM cannot assert this from their side — <c>UserManager</c> is Identity's.</summary>
    [Fact]
    public async Task Provisioned_user_holds_exactly_the_requested_role()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        await EnsureRolesAsync(scope.ServiceProvider);

        Result<string> result = await ProvisionAsync(scope.ServiceProvider, UserKind.Employee, IdentityRoles.Employee);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);

        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser user = (await userManager.FindByIdAsync(result.Value!))!;

        Assert.Equal([IdentityRoles.Employee], await userManager.GetRolesAsync(user));
    }

    [Fact]
    public async Task Duplicate_email_is_refused()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        await EnsureRolesAsync(scope.ServiceProvider);

        string email = $"provision-{Guid.NewGuid():N}@example.com";

        Result<string> first = await ProvisionAsync(scope.ServiceProvider, UserKind.Employee, IdentityRoles.Employee, email);
        Assert.True(first.IsSuccess, first.IsFailure ? first.Error.ToString() : null);

        Result<string> second = await ProvisionAsync(scope.ServiceProvider, UserKind.Employee, IdentityRoles.Employee, email);

        Assert.True(second.IsFailure);
        Assert.Equal(UserErrors.DuplicatedEmail(email).Code, second.Error.Code);
    }

    /// <summary>First coverage of <c>UserType.Client</c> in the repo — self-registration hard-coded
    /// <c>UserType.Employee</c>, so every CRM client this codebase ever created was stored as an
    /// employee.</summary>
    [Fact]
    public async Task Client_kind_persists_as_client_user_type()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        await EnsureRolesAsync(scope.ServiceProvider);

        Result<string> result = await ProvisionAsync(scope.ServiceProvider, UserKind.Client, IdentityRoles.Client);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);

        ApplicationUser client = await LoadAsync(scope.ServiceProvider, result.Value!);
        Assert.Equal(UserType.Client, client.UserType);

        // Paired with the Employee case so this asserts the mapping, not a constant.
        Result<string> employeeResult = await ProvisionAsync(scope.ServiceProvider, UserKind.Employee, IdentityRoles.Employee);
        ApplicationUser employee = await LoadAsync(scope.ServiceProvider, employeeResult.Value!);
        Assert.Equal(UserType.Employee, employee.UserType);
    }

    private static Task<Result<string>> ProvisionAsync(
        IServiceProvider services, UserKind kind, string roleName, string? email = null) =>
        services.GetRequiredService<IUserProvisioning>().CreateAsync(
            new NewUserRequest(
                email ?? $"provision-{Guid.NewGuid():N}@example.com",
                ValidPassword,
                "Test",
                "User",
                null,
                kind,
                roleName),
            CancellationToken.None);

    private static async Task<ApplicationUser> LoadAsync(IServiceProvider services, string userId) =>
        await services.GetRequiredService<IdentityModuleDbContext>()
            .Users.AsNoTracking().SingleAsync(u => u.Id == userId);

    /// <summary>This fixture builds its own container rather than running <c>IModule.SeedAsync</c>, so
    /// the roles provisioning resolves have to be put there by hand. Every default role, not only the
    /// two provisionable ones — the full-access refusal test needs <c>admin</c> and <c>manager</c> to
    /// genuinely exist for its assertion to mean anything.</summary>
    private static async Task EnsureRolesAsync(IServiceProvider services)
    {
        RoleManager<ApplicationRole> roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();

        foreach (string roleName in DefaultRoles.All)
        {
            if (await roleManager.FindByNameAsync(roleName) is not null)
            {
                continue;
            }

            IdentityResult created = await roleManager.CreateAsync(
                ApplicationRole.Create(Guid.NewGuid().ToString(), roleName));

            if (!created.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Could not seed role '{roleName}': {string.Join("; ", created.Errors.Select(e => e.Description))}");
            }
        }
    }
}
