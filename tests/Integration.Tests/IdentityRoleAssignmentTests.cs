using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Identity.Contracts;
using Identity.Contracts.Roles;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// B6's verify block. <c>IRoleService.AssignRoleToUserAsync</c>, <c>RemoveRoleFromUserAsync</c> and
/// <c>GetUserRolesAsync</c> already existed and had zero callers anywhere in the repository — a user's
/// roles could never be changed through the API. These three routes are the only thing that was
/// missing; nothing below the endpoint layer needed building.
///
/// <para>Every test creates its own throwaway role rather than asserting against a seeded one
/// (<c>employee</c>, <c>admin</c>, ...): <c>ApiFixture</c> never resets between test classes, so a
/// shared role name would make this suite's outcome depend on execution order.</para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class IdentityRoleAssignmentTests(ApiFixture fixture)
{
    /// <summary>
    /// The whole point of B6: assign, observe it through the read side, remove, observe it gone. A test
    /// that only checked the 204s would pass even if assignment silently no-op'd — <c>IRoleService</c>'s
    /// own methods are thin wrappers over ASP.NET Identity's <c>UserManager</c>, and this is what proves
    /// the wiring reaches it, not just that the route exists.
    /// </summary>
    [Fact]
    public async Task AssignRoleToUser_ThenRemove_RoundTripsThroughGetUserRoles()
    {
        string appUserId = await IdentityTestSeeding.ProvisionActiveUserAsync(
            fixture, $"b6-{Guid.NewGuid():N}@example.com");

        HttpClient client = fixture.CreateClient();
        string manageToken = fixture.IssueTestToken(Guid.NewGuid().ToString(), IdentityPermissions.RolesManage);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", manageToken);

        HttpResponseMessage created = await client.PostAsJsonAsync(
            "/api/roles", new { Name = $"b6-role-{Guid.NewGuid():N}"[..24] });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        string roleId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("roleId").GetString()!;

        HttpResponseMessage assigned = await client.PostAsync($"/api/roles/{roleId}/users/{appUserId}", content: null);
        Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);

        string readToken = fixture.IssueTestToken(Guid.NewGuid().ToString(), IdentityPermissions.RolesRead);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readToken);

        List<RoleResponse>? afterAssign =
            await client.GetFromJsonAsync<List<RoleResponse>>($"/api/roles/users/{appUserId}");
        Assert.Contains(afterAssign!, r => r.Id == roleId);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", manageToken);
        HttpResponseMessage removed = await client.DeleteAsync($"/api/roles/{roleId}/users/{appUserId}");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readToken);
        List<RoleResponse>? afterRemove =
            await client.GetFromJsonAsync<List<RoleResponse>>($"/api/roles/users/{appUserId}");
        Assert.DoesNotContain(afterRemove!, r => r.Id == roleId);
    }

    /// <summary>
    /// A deliberate choice, pinned the same way B1's dead-validator gap was: <c>GetUserRolesAsync</c>
    /// returns an empty list for "no such user" and for "a real user with no roles" alike, and cannot
    /// tell the two apart. Rather than have the handler invent a 404 the service cannot substantiate,
    /// this stays a thin passthrough like every other Role handler — so an id nobody ever provisioned
    /// answers <c>200 []</c>, not 404. Callers on the frontend: an empty array is not proof the user
    /// exists.
    /// </summary>
    [Fact]
    public async Task GetUserRoles_ForAnIdNobodyProvisioned_Is200WithAnEmptyArray_NotNotFound()
    {
        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString(), IdentityPermissions.RolesRead);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.GetAsync($"/api/roles/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<RoleResponse>? roles = await response.Content.ReadFromJsonAsync<List<RoleResponse>>();
        Assert.NotNull(roles);
        Assert.Empty(roles!);
    }

    /// <summary>
    /// <c>CLAUDE.md</c>'s authorization order: route claims (403) before anything else runs. Assigning a
    /// role needs <c>roles.manage</c>; holding only <c>roles.read</c> must not be enough, or the read/
    /// write split this module draws everywhere else stops meaning anything here.
    /// </summary>
    [Fact]
    public async Task AssignRoleToUser_WithOnlyRolesRead_Is403()
    {
        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString(), IdentityPermissions.RolesRead);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.PostAsync(
            $"/api/roles/{Guid.NewGuid()}/users/{Guid.NewGuid()}", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
