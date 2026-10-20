using BuildingBlocks.Messaging;
using Identity.Contracts;
using Identity.Contracts.Permissions;
using Identity.Domain;
using Identity.Infrastructure.Authentication;
using Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel;
using Xunit;

namespace Identity.Application.Tests;

/// <summary>
/// Identity Phase 3:
/// <list type="bullet">
/// <item>role-claim removal outcomes (Aug 3.2, B5, C2);</item>
/// <item>assign is idempotent (Aug 3.7);</item>
/// <item>revoke goes through the change tracker and still 404s on a missing row;</item>
/// <item>the user permission list has one row per code (Aug 3.3);</item>
/// <item>unique-index races answer the check's error, deterministically through
/// <see cref="PlantBeforeSave"/> and concurrently as an outcome set (Aug 3.5, B2);</item>
/// <item>one password policy (N9, B4);</item>
/// <item><c>UpdateNameAsync</c> (N7, B9).</item>
/// </list>
/// The fixture's catalog is Identity's own four codes, so <c>"stale.code"</c> is a code nobody
/// registers.
/// </summary>
[Collection(IdentityApplicationCollection.Name)]
public sealed class Phase3IdentityTests(IdentityApplicationFixture fixture)
{
    private const string Known = IdentityPermissions.UsersRead;

    // --- Aug 3.2: removing a permission from a role ---

    [Fact]
    public async Task Removing_a_held_permission_removes_it_and_bumps_the_holders()
    {
        (string roleId, string holderId) = await CreateRoleWithHolderAsync(Known);
        string stampBefore = await StampAsync(holderId);

        Result result = await SendAsync(new RemovePermissionFromRoleCommand(roleId, Known));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        Assert.Empty(await ClaimsAsync(roleId));
        Assert.NotEqual(stampBefore, await StampAsync(holderId));
    }

    /// <summary>C2: a claim whose code the catalog no longer knows must still be removable.</summary>
    [Fact]
    public async Task Removing_a_held_stale_code_removes_it()
    {
        (string roleId, _) = await CreateRoleWithHolderAsync("stale.code");

        Result result = await SendAsync(new RemovePermissionFromRoleCommand(roleId, "stale.code"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        Assert.Empty(await ClaimsAsync(roleId));
    }

    [Fact]
    public async Task Removing_a_known_permission_the_role_does_not_hold_is_a_no_op_without_a_bump()
    {
        (string roleId, string holderId) = await CreateRoleWithHolderAsync();
        string stampBefore = await StampAsync(holderId);

        Result result = await SendAsync(new RemovePermissionFromRoleCommand(roleId, Known));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        Assert.Equal(stampBefore, await StampAsync(holderId));
    }

    [Fact]
    public async Task Removing_an_unknown_code_the_role_does_not_hold_is_400()
    {
        (string roleId, _) = await CreateRoleWithHolderAsync();

        Result result = await SendAsync(new RemovePermissionFromRoleCommand(roleId, "no.such.code"));

        Assert.Equal("permissions.InvalidPermission", result.Error.Code);
        Assert.Equal(400, result.Error.Status);
    }

    [Fact]
    public async Task Removing_from_an_unknown_role_is_404()
    {
        Result result = await SendAsync(new RemovePermissionFromRoleCommand(Guid.NewGuid().ToString(), Known));

        Assert.Equal(404, result.Error.Status);
    }

    // --- Aug 3.7: assign ---

    [Fact]
    public async Task Assigning_a_held_permission_succeeds_without_a_bump()
    {
        (string roleId, string holderId) = await CreateRoleWithHolderAsync(Known);
        string stampBefore = await StampAsync(holderId);

        Result result = await SendAsync(new AssignPermissionToRoleCommand(roleId, Known));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        Assert.Single(await ClaimsAsync(roleId));
        Assert.Equal(stampBefore, await StampAsync(holderId));
    }

    [Fact]
    public async Task Assigning_an_unknown_code_is_400()
    {
        (string roleId, _) = await CreateRoleWithHolderAsync();

        Result result = await SendAsync(new AssignPermissionToRoleCommand(roleId, "no.such.code"));

        Assert.True(result.IsFailure);
        Assert.Equal(400, result.Error.Status);
    }

    // --- Aug 3.2: revoke ---

    [Fact]
    public async Task Revoking_a_missing_override_is_404_and_for_a_missing_user_too()
    {
        (_, string holderId) = await CreateRoleWithHolderAsync();

        Result missingRow = await SendAsync(new RevokePermissionOverrideCommand(holderId, Known));
        Result missingUser = await SendAsync(new RevokePermissionOverrideCommand(Guid.NewGuid().ToString(), Known));

        Assert.Equal("permissions.PermissionOverrideNotFound", missingRow.Error.Code);
        Assert.Equal("users.NotFound", missingUser.Error.Code);
    }

    [Fact]
    public async Task Revoking_an_override_removes_the_row_and_bumps_the_target()
    {
        (_, string holderId) = await CreateRoleWithHolderAsync();
        Assert.True((await SendAsync(new GrantPermissionOverrideCommand(holderId, Known))).IsSuccess);
        string stampBefore = await StampAsync(holderId);

        Result result = await SendAsync(new RevokePermissionOverrideCommand(holderId, Known));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        await using AsyncServiceScope scope = fixture.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>()
            .UserPermissionOverrides.AnyAsync(o => o.UserId == holderId));
        Assert.NotEqual(stampBefore, await StampAsync(holderId));
    }

    // --- Aug 3.3: dedup ---

    [Fact]
    public async Task A_permission_held_through_two_roles_is_listed_once()
    {
        (string firstRole, string holderId) = await CreateRoleWithHolderAsync(Known);
        (string secondRole, _) = await CreateRoleWithHolderAsync(Known);
        await AddToRoleAsync(holderId, secondRole);

        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<IReadOnlyList<PermissionResponse>> result = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetUserPermissionsQuery, IReadOnlyList<PermissionResponse>>>()
            .HandleAsync(new GetUserPermissionsQuery(holderId));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
        Assert.Single(result.Value!, p => p.ClaimValue == Known);
        Assert.NotEqual(firstRole, secondRole);
    }

    // --- Aug 3.5: races (B2) ---

    /// <summary>The override row appears between the pre-check and the insert. Without the 23505
    /// mapping this is a <c>DbUpdateException</c> (500).</summary>
    [Fact]
    public async Task A_grant_that_loses_the_race_to_the_unique_index_is_409()
    {
        (_, string holderId) = await CreateRoleWithHolderAsync();
        fixture.Plant.Arm(fixture.ConnectionString, $"""
            INSERT INTO identity.user_permission_overrides
                ("Id", "UserId", "Permission", "IsGranted", "GrantedById", "IsActive", "CreatedAtUtc")
            VALUES (gen_random_uuid(), '{holderId}', '{Known}', true, '{IdentityApplicationFixture.CallerId}', true, now());
            """);

        try
        {
            Result result = await SendAsync(new GrantPermissionOverrideCommand(holderId, Known));

            Assert.Equal("permissions.PermissionOverrideAlreadyExists", result.Error.Code);
            Assert.Equal(409, result.Error.Status);
        }
        finally
        {
            fixture.Plant.Disarm();
        }
    }

    /// <summary>A login with the same user name appears between provisioning's email check and its
    /// insert. <c>UserNameIndex</c> is the unique index that catches it (EmailIndex is not unique).</summary>
    [Fact]
    public async Task Provisioning_that_loses_the_race_to_UserNameIndex_is_DuplicatedEmail()
    {
        await EnsureRoleAsync(IdentityRoles.Employee);
        string email = $"race-{Guid.NewGuid():N}@example.com";
        fixture.Plant.Arm(fixture.ConnectionString, $"""
            INSERT INTO identity.users
                ("Id", "UserName", "NormalizedUserName", "FirstName", "LastName", "IsActive", "UserType", "CreatedAt",
                 "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
            VALUES ('{Guid.NewGuid()}', '{email}', '{email.ToUpperInvariant()}', 'R', 'R', true, 0, now(),
                    false, false, false, true, 0);
            """);

        try
        {
            await using AsyncServiceScope scope = fixture.CreateScope();
            Result<string> result = await scope.ServiceProvider.GetRequiredService<IUserProvisioning>().CreateAsync(
                new NewUserRequest(email, IdentityTestSeeding.Password, "R", "R", null, UserKind.Employee, IdentityRoles.Employee),
                CancellationToken.None);

            Assert.Equal("users.DuplicatedEmail", result.Error.Code);
        }
        finally
        {
            fixture.Plant.Disarm();
        }
    }

    [Fact]
    public async Task Two_concurrent_grants_give_one_success_and_one_409_never_a_500()
    {
        (_, string holderId) = await CreateRoleWithHolderAsync();

        Result[] results = await Task.WhenAll(
            SendAsync(new GrantPermissionOverrideCommand(holderId, Known)),
            SendAsync(new GrantPermissionOverrideCommand(holderId, Known)));

        Assert.Single(results, r => r.IsSuccess);
        Assert.Single(results, r => r.IsFailure && r.Error.Status == 409);
    }

    // --- N9 (B4): one password policy ---

    [Theory]
    [InlineData("P@ssw0rd123!")]
    [InlineData("Aa1!aaaa")]
    [InlineData("Aa1!aaa")] // 7 characters
    [InlineData("aa1!aaaa")] // no upper-case
    [InlineData("AA1!AAAA")] // no lower-case
    [InlineData("Aa!!aaaa")] // no digit
    [InlineData("Aa11aaaa")] // no symbol
    [InlineData("Aa1 aaaa")] // a space is a symbol
    [InlineData("")]
    public async Task IdentityPasswordPolicy_agrees_with_the_configured_IdentityOptions(string password)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = ApplicationUser.Create("p", "p@example.com", "p@example.com", "P", "P", UserType.Employee);

        bool identityAccepts = true;
        foreach (IPasswordValidator<ApplicationUser> validator in userManager.PasswordValidators)
        {
            identityAccepts &= (await validator.ValidateAsync(userManager, user, password)).Succeeded;
        }

        Assert.Equal(identityAccepts, IdentityPasswordPolicy.IsSatisfiedBy(password));
        Assert.Equal(IdentityPasswordPolicy.RequiredLength,
            scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value.Password.RequiredLength);
        Assert.Equal(IdentityPolicy.RequiredPasswordLength, IdentityPasswordPolicy.RequiredLength);
    }

    // --- N7 (B9): UpdateNameAsync ---

    [Fact]
    public async Task UpdateNameAsync_trims_is_idempotent_and_refuses_bad_names()
    {
        (_, string userId) = await CreateRoleWithHolderAsync();
        string stampBefore = await StampAsync(userId);
        await using AsyncServiceScope scope = fixture.CreateScope();
        IUserAdministration administration = scope.ServiceProvider.GetRequiredService<IUserAdministration>();

        Result first = await administration.UpdateNameAsync(userId, "  Jane ", " Doe ", CancellationToken.None);
        Result again = await administration.UpdateNameAsync(userId, "Jane", "Doe", CancellationToken.None);
        Result empty = await administration.UpdateNameAsync(userId, " ", "Doe", CancellationToken.None);
        Result tooLong = await administration.UpdateNameAsync(userId, new string('x', 101), "Doe", CancellationToken.None);
        Result missing = await administration.UpdateNameAsync(Guid.NewGuid().ToString(), "A", "B", CancellationToken.None);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error.ToString() : null);
        Assert.True(again.IsSuccess);
        Assert.Equal("users.InvalidName", empty.Error.Code);
        Assert.Equal("users.InvalidName", tooLong.Error.Code);
        Assert.Equal("users.NotFound", missing.Error.Code);

        await using AsyncServiceScope read = fixture.CreateScope();
        ApplicationUser stored = await read.ServiceProvider.GetRequiredService<IdentityModuleDbContext>()
            .Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        Assert.Equal(("Jane", "Doe"), (stored.FirstName, stored.LastName));
        Assert.Equal(stampBefore, stored.SecurityStamp);
    }

    // --- helpers ---

    /// <summary>A fresh role holding <paramref name="codes"/> (written straight to the table, so a stale
    /// code can be planted), and a fresh active user in it.</summary>
    private async Task<(string RoleId, string HolderId)> CreateRoleWithHolderAsync(params string[] codes)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        RoleManager<ApplicationRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        IdentityModuleDbContext db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();

        var role = ApplicationRole.Create(Guid.NewGuid().ToString(), $"p3-{Guid.NewGuid():N}");
        await roleManager.CreateAsync(role);
        foreach (string code in codes)
        {
            db.RoleClaims.Add(ApplicationRoleClaim.Create(role.Id, BuildingBlocks.PermissionClaim.Type, code));
        }

        await db.SaveChangesAsync();

        string userId = Guid.NewGuid().ToString();
        var user = ApplicationUser.Create(
            userId, $"p3-{userId}@example.com", $"p3-{userId}@example.com", "P", "Three", UserType.Employee);
        Assert.True((await userManager.CreateAsync(user, IdentityTestSeeding.Password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(user, role.Name!)).Succeeded);
        return (role.Id, userId);
    }

    private async Task AddToRoleAsync(string userId, string roleId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IdentityModuleDbContext db = scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>();
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = userId, RoleId = roleId });
        await db.SaveChangesAsync();
    }

    private async Task EnsureRoleAsync(string name)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        RoleManager<ApplicationRole> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        if (!await roleManager.RoleExistsAsync(name))
        {
            await roleManager.CreateAsync(ApplicationRole.Create(Guid.NewGuid().ToString(), name));
        }
    }

    private async Task<Result> SendAsync<TCommand>(TCommand command)
        where TCommand : ICommand
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(command);
    }

    private async Task<List<string>> ClaimsAsync(string roleId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>().RoleClaims
            .Where(rc => rc.RoleId == roleId).Select(rc => rc.ClaimValue!).ToListAsync();
    }

    private async Task<string> StampAsync(string userId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IdentityModuleDbContext>()
            .Users.AsNoTracking().SingleAsync(u => u.Id == userId)).SecurityStamp!;
    }
}
