using BuildingBlocks.Messaging;
using Identity.Contracts;
using Identity.Contracts.Permissions;
using Identity.Contracts.Roles;
using Identity.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;
using Xunit;

namespace Identity.Application.Tests;

[Collection(IdentityApplicationCollection.Name)]
public sealed class RolesAndPermissionsTests(IdentityApplicationFixture fixture)
{
    [Fact]
    public async Task GetAllPermissions_returns_the_module_permission_catalog_union()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        IQueryHandler<GetAllPermissionsQuery, IReadOnlyList<ModulePermissionSet>> handler = sp.GetRequiredService<IQueryHandler<GetAllPermissionsQuery, IReadOnlyList<ModulePermissionSet>>>();
        Result<IReadOnlyList<ModulePermissionSet>> result = await handler.HandleAsync(new GetAllPermissionsQuery());

        Assert.True(result.IsSuccess);
        var allPermissions = result.Value!.SelectMany(m => m.Permissions).ToList();
        Assert.Contains(IdentityPermissions.UsersRead, allPermissions);
        Assert.Contains(IdentityPermissions.RolesManage, allPermissions);
        Assert.Contains(IdentityPermissions.PermissionsManage, allPermissions);
    }

    [Fact]
    public async Task Create_role_then_assign_and_remove_permission_round_trips()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        string roleName = $"role-{Guid.NewGuid():N}";

        ICommandHandler<CreateRoleCommand, string> createRole = sp.GetRequiredService<ICommandHandler<CreateRoleCommand, string>>();
        Result<string> createResult = await createRole.HandleAsync(new CreateRoleCommand(roleName));
        Assert.True(createResult.IsSuccess);
        string roleId = createResult.Value!;

        ICommandHandler<AssignPermissionToRoleCommand> assign = sp.GetRequiredService<ICommandHandler<AssignPermissionToRoleCommand>>();
        Result assignResult = await assign.HandleAsync(
            new AssignPermissionToRoleCommand(roleId, IdentityPermissions.UsersRead));
        Assert.True(assignResult.IsSuccess);

        IQueryHandler<GetRolePermissionsQuery, IReadOnlyList<PermissionResponse>> getRolePermissions = sp.GetRequiredService<IQueryHandler<GetRolePermissionsQuery, IReadOnlyList<PermissionResponse>>>();
        Result<IReadOnlyList<PermissionResponse>> afterAssign = await getRolePermissions.HandleAsync(new GetRolePermissionsQuery(roleId));
        Assert.True(afterAssign.IsSuccess);
        Assert.Contains(afterAssign.Value!, p => p.ClaimValue == IdentityPermissions.UsersRead);

        ICommandHandler<RemovePermissionFromRoleCommand> remove = sp.GetRequiredService<ICommandHandler<RemovePermissionFromRoleCommand>>();
        Result removeResult = await remove.HandleAsync(
            new RemovePermissionFromRoleCommand(roleId, IdentityPermissions.UsersRead));
        Assert.True(removeResult.IsSuccess);

        Result<IReadOnlyList<PermissionResponse>> afterRemove = await getRolePermissions.HandleAsync(new GetRolePermissionsQuery(roleId));
        Assert.DoesNotContain(afterRemove.Value!, p => p.ClaimValue == IdentityPermissions.UsersRead);
    }

    [Fact]
    public async Task AssignPermissionToRole_with_unknown_permission_is_rejected()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        RoleManager<ApplicationRole> roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        var role = ApplicationRole.Create(Guid.NewGuid().ToString(), $"role-{Guid.NewGuid():N}");
        await roleManager.CreateAsync(role);

        ICommandHandler<AssignPermissionToRoleCommand> assign = sp.GetRequiredService<ICommandHandler<AssignPermissionToRoleCommand>>();
        Result result = await assign.HandleAsync(new AssignPermissionToRoleCommand(role.Id, "not.a.real.permission"));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task GetUserPermissions_reflects_the_users_role_claims()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        RoleManager<ApplicationRole> roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        var role = ApplicationRole.Create(Guid.NewGuid().ToString(), $"role-{Guid.NewGuid():N}");
        await roleManager.CreateAsync(role);

        ICommandHandler<AssignPermissionToRoleCommand> assign = sp.GetRequiredService<ICommandHandler<AssignPermissionToRoleCommand>>();
        await assign.HandleAsync(new AssignPermissionToRoleCommand(role.Id, IdentityPermissions.RolesRead));

        var user = ApplicationUser.Create(
            Guid.NewGuid().ToString(), $"perm-user-{Guid.NewGuid():N}@example.com",
            $"perm-user-{Guid.NewGuid():N}@example.com", "Test", "User", UserType.Employee);
        await userManager.CreateAsync(user, "P@ssw0rd123!");
        await userManager.AddToRoleAsync(user, role.Name!);

        IQueryHandler<GetUserPermissionsQuery, IReadOnlyList<PermissionResponse>> getUserPermissions = sp.GetRequiredService<IQueryHandler<GetUserPermissionsQuery, IReadOnlyList<PermissionResponse>>>();
        Result<IReadOnlyList<PermissionResponse>> result = await getUserPermissions.HandleAsync(new GetUserPermissionsQuery(user.Id));

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value!, p => p.ClaimValue == IdentityPermissions.RolesRead);
    }

    [Fact]
    public async Task Grant_then_deny_then_revoke_override_each_change_the_effective_set()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        RoleManager<ApplicationRole> roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        UserManager<ApplicationUser> userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        var role = ApplicationRole.Create(Guid.NewGuid().ToString(), $"role-{Guid.NewGuid():N}");
        await roleManager.CreateAsync(role);

        ICommandHandler<AssignPermissionToRoleCommand> assign = sp.GetRequiredService<ICommandHandler<AssignPermissionToRoleCommand>>();
        await assign.HandleAsync(new AssignPermissionToRoleCommand(role.Id, IdentityPermissions.RolesRead));

        var user = ApplicationUser.Create(
            Guid.NewGuid().ToString(), $"override-user-{Guid.NewGuid():N}@example.com",
            $"override-user-{Guid.NewGuid():N}@example.com", "Test", "User", UserType.Employee);
        await userManager.CreateAsync(user, "P@ssw0rd123!");
        await userManager.AddToRoleAsync(user, role.Name!);

        // PF-D1: another user's effective set needs permissions.manage, as through the API. A reader scope
        // of its own, so the commands above keep the fixture's default caller.
        await using AsyncServiceScope readerScope = fixture.CreateScope(
            new TestCurrentUser(IdentityApplicationFixture.CallerId, IdentityPermissions.PermissionsManage));
        IQueryHandler<GetUserEffectivePermissionsQuery, EffectivePermissionsResponse> getEffective = readerScope.ServiceProvider.GetRequiredService<IQueryHandler<GetUserEffectivePermissionsQuery, EffectivePermissionsResponse>>();

        // Role alone grants RolesRead but not UsersRead.
        Result<EffectivePermissionsResponse> baseline = await getEffective.HandleAsync(new GetUserEffectivePermissionsQuery(user.Id));
        Assert.Contains(IdentityPermissions.RolesRead, baseline.Value!.Permissions);
        Assert.DoesNotContain(IdentityPermissions.UsersRead, baseline.Value!.Permissions);

        // Grant override adds UsersRead on top of the role.
        ICommandHandler<GrantPermissionOverrideCommand> grant = sp.GetRequiredService<ICommandHandler<GrantPermissionOverrideCommand>>();
        Result grantResult = await grant.HandleAsync(new GrantPermissionOverrideCommand(user.Id, IdentityPermissions.UsersRead));
        Assert.True(grantResult.IsSuccess);

        Result<EffectivePermissionsResponse> afterGrant = await getEffective.HandleAsync(new GetUserEffectivePermissionsQuery(user.Id));
        Assert.Contains(IdentityPermissions.UsersRead, afterGrant.Value!.Permissions);

        // Deny override on the role-granted permission removes it despite the role.
        ICommandHandler<DenyPermissionOverrideCommand> deny = sp.GetRequiredService<ICommandHandler<DenyPermissionOverrideCommand>>();
        Result denyResult = await deny.HandleAsync(new DenyPermissionOverrideCommand(user.Id, IdentityPermissions.RolesRead));
        Assert.True(denyResult.IsSuccess);

        Result<EffectivePermissionsResponse> afterDeny = await getEffective.HandleAsync(new GetUserEffectivePermissionsQuery(user.Id));
        Assert.DoesNotContain(IdentityPermissions.RolesRead, afterDeny.Value!.Permissions);
        Assert.Equal(2, afterDeny.Value!.Overrides.Count);

        // Revoking the deny override restores the role-derived permission.
        ICommandHandler<RevokePermissionOverrideCommand> revoke = sp.GetRequiredService<ICommandHandler<RevokePermissionOverrideCommand>>();
        Result revokeResult = await revoke.HandleAsync(new RevokePermissionOverrideCommand(user.Id, IdentityPermissions.RolesRead));
        Assert.True(revokeResult.IsSuccess);

        Result<EffectivePermissionsResponse> afterRevoke = await getEffective.HandleAsync(new GetUserEffectivePermissionsQuery(user.Id));
        Assert.Contains(IdentityPermissions.RolesRead, afterRevoke.Value!.Permissions);
        Assert.Single(afterRevoke.Value!.Overrides);
    }

    [Fact]
    public async Task Update_role_renames_and_changes_description_then_delete_removes_it()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;

        ICommandHandler<CreateRoleCommand, string> createRole = sp.GetRequiredService<ICommandHandler<CreateRoleCommand, string>>();
        Result<string> createResult = await createRole.HandleAsync(new CreateRoleCommand($"role-{Guid.NewGuid():N}"));
        string roleId = createResult.Value!;

        string newName = $"renamed-{Guid.NewGuid():N}";
        ICommandHandler<UpdateRoleCommand> update = sp.GetRequiredService<ICommandHandler<UpdateRoleCommand>>();
        Result updateResult = await update.HandleAsync(new UpdateRoleCommand(roleId, newName, "a new description"));
        Assert.True(updateResult.IsSuccess);

        IQueryHandler<GetRoleQuery, RoleResponse> getRole = sp.GetRequiredService<IQueryHandler<GetRoleQuery, RoleResponse>>();
        Result<RoleResponse> afterUpdate = await getRole.HandleAsync(new GetRoleQuery(roleId));
        Assert.True(afterUpdate.IsSuccess);
        Assert.Equal(newName, afterUpdate.Value!.Name);
        Assert.Equal("a new description", afterUpdate.Value!.Description);

        IQueryHandler<GetAllRolesQuery, IReadOnlyList<RoleResponse>> getAllRoles = sp.GetRequiredService<IQueryHandler<GetAllRolesQuery, IReadOnlyList<RoleResponse>>>();

        // PC-03 liveness, as a before/after pair rather than a bare non-empty check: this proves
        // GetAllRolesQuery can see THIS role, so its absence afterwards is the delete's doing. A query
        // that always returned nothing would satisfy the assertion below on its own.
        Result<IReadOnlyList<RoleResponse>> beforeDelete = await getAllRoles.HandleAsync(new GetAllRolesQuery());
        Assert.Contains(beforeDelete.Value!, r => r.Id == roleId);

        ICommandHandler<DeleteRoleCommand> delete = sp.GetRequiredService<ICommandHandler<DeleteRoleCommand>>();
        Result deleteResult = await delete.HandleAsync(new DeleteRoleCommand(roleId));
        Assert.True(deleteResult.IsSuccess);

        Result<RoleResponse> afterDelete = await getRole.HandleAsync(new GetRoleQuery(roleId));
        Assert.True(afterDelete.IsFailure);

        Result<IReadOnlyList<RoleResponse>> allRoles = await getAllRoles.HandleAsync(new GetAllRolesQuery());
        Assert.DoesNotContain(allRoles.Value!, r => r.Id == roleId);
    }
}
