using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Auditing;
using BuildingBlocks.Outbox;
using Identity.Contracts;
using Identity.Contracts.Permissions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// HR/Identity Phase 1A, through the whole HTTP pipeline with real login tokens (<see cref="TestUsers"/>):
/// <list type="bullet">
/// <item>N1: the delegation guard;</item>
/// <item>K6: no self-modification;</item>
/// <item>K8: the effective-permission reads;</item>
/// <item>N3: full-access roles locked;</item>
/// <item>N4: system roles, roles in use, audited deletes.</item>
/// </list>
/// Each test makes its own users and roles: <see cref="ApiFixture"/> is shared and never reset.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class IdentityAuthorizationTests(ApiFixture fixture)
{
    // --- N1: the delegation guard ---

    [Fact]
    public async Task RolesManageOnly_CannotAssignTheAdminRole()
    {
        TestSession caller = await TestUsers.CreateUserWithPermissionsAsync(fixture, IdentityPermissions.RolesManage);
        TestSession target = await TestUsers.CreateUserWithPermissionsAsync(fixture);
        HttpClient admin = await TestUsers.AdminClientAsync(fixture);
        string adminRoleId = await TestUsers.RoleIdAsync(admin, "admin");

        HttpResponseMessage toTarget = await caller.Client.PostAsync($"/api/roles/{adminRoleId}/users/{target.UserId}", null);
        HttpResponseMessage toSelf = await caller.Client.PostAsync($"/api/roles/{adminRoleId}/users/{caller.UserId}", null);

        await AssertProblemAsync(toTarget, HttpStatusCode.Forbidden, "permissions.CannotDelegate");
        await AssertProblemAsync(toSelf, HttpStatusCode.Forbidden, "permissions.CannotDelegate");
    }

    [Fact]
    public async Task RolesManageOnly_CannotAddAPermissionTheyLack_EvenToTheirOwnRole()
    {
        TestSession caller = await TestUsers.CreateUserWithPermissionsAsync(fixture, IdentityPermissions.RolesManage);

        HttpResponseMessage response = await caller.Client.PutAsync(
            $"/api/permissions/roles/{caller.RoleId}/{IdentityPermissions.PermissionsManage}", null);

        string body = await AssertProblemAsync(response, HttpStatusCode.Forbidden, "permissions.CannotDelegate");
        Assert.Contains(IdentityPermissions.PermissionsManage, body);
    }

    [Fact]
    public async Task RolesManageOnly_CanAssignARoleWhoseClaimsTheyHold()
    {
        TestSession caller = await TestUsers.CreateUserWithPermissionsAsync(
            fixture, IdentityPermissions.RolesManage, IdentityPermissions.RolesRead);
        TestSession target = await TestUsers.CreateUserWithPermissionsAsync(fixture);
        HttpClient admin = await TestUsers.AdminClientAsync(fixture);
        string roleId = await TestUsers.CreateRoleAsync(admin, IdentityPermissions.RolesRead);

        HttpResponseMessage response = await caller.Client.PostAsync($"/api/roles/{roleId}/users/{target.UserId}", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task PermissionsManage_CannotGrantAnOverrideTheyDoNotHold()
    {
        TestSession caller = await TestUsers.CreateUserWithPermissionsAsync(fixture, IdentityPermissions.PermissionsManage);
        TestSession target = await TestUsers.CreateUserWithPermissionsAsync(fixture);

        HttpResponseMessage response = await caller.Client.PostAsJsonAsync(
            $"/api/permissions/users/{target.UserId}/overrides/grant", new { Permission = IdentityPermissions.RolesManage });

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "permissions.CannotDelegate");
    }

    // --- K6: overrides on yourself ---

    [Fact]
    public async Task GrantingAnOverrideToYourself_Is403()
    {
        TestSession caller = await TestUsers.CreateUserWithPermissionsAsync(
            fixture, IdentityPermissions.PermissionsManage, IdentityPermissions.UsersRead);

        HttpResponseMessage grant = await caller.Client.PostAsJsonAsync(
            $"/api/permissions/users/{caller.UserId}/overrides/grant", new { Permission = IdentityPermissions.UsersRead });
        HttpResponseMessage revoke = await caller.Client.DeleteAsync(
            $"/api/permissions/users/{caller.UserId}/overrides/{IdentityPermissions.UsersRead}");

        await AssertProblemAsync(grant, HttpStatusCode.Forbidden, "permissions.SelfModification");
        await AssertProblemAsync(revoke, HttpStatusCode.Forbidden, "permissions.SelfModification");
    }

    // --- K8: effective permissions ---

    [Fact]
    public async Task PlainEmployee_CannotReadAnotherUsersEffectivePermissions_ButCanReadTheirOwn()
    {
        TestSession employee = await TestUsers.CreateUserInRoleAsync(fixture, IdentityRoles.Employee);
        TestSession other = await TestUsers.CreateUserWithPermissionsAsync(fixture, IdentityPermissions.RolesRead);

        HttpResponseMessage others = await employee.Client.GetAsync($"/api/permissions/users/{other.UserId}/effective");
        HttpResponseMessage mine = await other.Client.GetAsync("/api/permissions/me/effective");

        Assert.Equal(HttpStatusCode.Forbidden, others.StatusCode);
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        EffectivePermissionsResponse body = (await mine.Content.ReadFromJsonAsync<EffectivePermissionsResponse>())!;
        Assert.Equal(other.UserId, body.UserId);
        Assert.Equal([IdentityPermissions.RolesRead], body.Permissions);
    }

    // --- N3: full-access roles are locked ---

    [Fact]
    public async Task EditingTheManagerRolesClaims_Is409_EvenForAnAdmin()
    {
        HttpClient admin = await TestUsers.AdminClientAsync(fixture);
        string managerRoleId = await TestUsers.RoleIdAsync(admin, "manager");

        HttpResponseMessage remove = await admin.DeleteAsync(
            $"/api/permissions/roles/{managerRoleId}/{IdentityPermissions.RolesRead}");
        HttpResponseMessage add = await admin.PutAsync(
            $"/api/permissions/roles/{managerRoleId}/{IdentityPermissions.RolesRead}", null);

        await AssertProblemAsync(remove, HttpStatusCode.Conflict, "roles.FullAccessLocked");
        await AssertProblemAsync(add, HttpStatusCode.Conflict, "roles.FullAccessLocked");
        JsonElement claims = await admin.GetFromJsonAsync<JsonElement>($"/api/permissions/roles/{managerRoleId}");
        Assert.Contains(claims.EnumerateArray(), c => c.GetProperty("claimValue").GetString() == IdentityPermissions.RolesRead);
    }

    [Fact]
    public async Task EditingTheManagerRole_ByANonFullAccessCaller_Is403_BeforeTheLock()
    {
        TestSession caller = await TestUsers.CreateUserWithPermissionsAsync(
            fixture, IdentityPermissions.RolesManage, IdentityPermissions.RolesRead);
        HttpClient admin = await TestUsers.AdminClientAsync(fixture);
        string managerRoleId = await TestUsers.RoleIdAsync(admin, "manager");

        HttpResponseMessage remove = await caller.Client.DeleteAsync(
            $"/api/permissions/roles/{managerRoleId}/{IdentityPermissions.RolesRead}");

        await AssertProblemAsync(remove, HttpStatusCode.Forbidden, "permissions.CannotDelegate");
    }

    // --- N4: system roles, roles in use, audited deletes ---

    [Fact]
    public async Task DeletingTheEmployeeRole_Is409()
    {
        HttpClient admin = await TestUsers.AdminClientAsync(fixture);
        string employeeRoleId = await TestUsers.RoleIdAsync(admin, IdentityRoles.Employee);

        HttpResponseMessage response = await admin.DeleteAsync($"/api/roles/{employeeRoleId}");

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "roles.SystemRole");
    }

    [Fact]
    public async Task RenamingTheClientRole_Is409_ButItsDescriptionIsEditable()
    {
        HttpClient admin = await TestUsers.AdminClientAsync(fixture);
        string clientRoleId = await TestUsers.RoleIdAsync(admin, IdentityRoles.Client);

        HttpResponseMessage rename = await admin.PutAsJsonAsync(
            $"/api/roles/{clientRoleId}", new { Name = "customer", Description = (string?)null });
        HttpResponseMessage describe = await admin.PutAsJsonAsync(
            $"/api/roles/{clientRoleId}", new { Name = IdentityRoles.Client, Description = "Portal users." });

        await AssertProblemAsync(rename, HttpStatusCode.Conflict, "roles.SystemRole");
        Assert.Equal(HttpStatusCode.NoContent, describe.StatusCode);
    }

    [Fact]
    public async Task DeletingARoleWithAMember_Is409()
    {
        TestSession member = await TestUsers.CreateUserWithPermissionsAsync(fixture, IdentityPermissions.RolesRead);
        HttpClient admin = await TestUsers.AdminClientAsync(fixture);

        HttpResponseMessage response = await admin.DeleteAsync($"/api/roles/{member.RoleId}");

        string body = await AssertProblemAsync(response, HttpStatusCode.Conflict, "roles.RoleInUse");
        Assert.Contains("1 user", body);
    }

    [Fact]
    public async Task DeletingAnUnusedRole_WritesAnAuditRowPerClaim()
    {
        HttpClient admin = await TestUsers.AdminClientAsync(fixture);
        string roleId = await TestUsers.CreateRoleAsync(admin, IdentityPermissions.RolesRead, IdentityPermissions.UsersRead);

        HttpResponseMessage response = await admin.DeleteAsync($"/api/roles/{roleId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        SharedDbContext shared = scope.ServiceProvider.GetRequiredService<SharedDbContext>();
        // OldValues is jsonb, so the role id is matched after the read, not in SQL.
        List<AuditLog> deletedClaims = [.. (await shared.AuditLogs.AsNoTracking()
                .Where(a => a.EntityName == "ApplicationRoleClaim" && a.Action == AuditAction.Delete)
                .ToListAsync())
            .Where(a => a.OldValues!.Contains(roleId))];
        Assert.Equal(2, deletedClaims.Count);
        Assert.Contains(deletedClaims, a => a.OldValues!.Contains(IdentityPermissions.RolesRead));
        Assert.Contains(deletedClaims, a => a.OldValues!.Contains(IdentityPermissions.UsersRead));
    }

    private static async Task<string> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {(int)status} {code}, got {(int)response.StatusCode}: {body}");
        Assert.Contains(code, body);
        return body;
    }
}
