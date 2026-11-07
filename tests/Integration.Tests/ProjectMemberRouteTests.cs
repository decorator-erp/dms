using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// E1 (D1): project members. A member is part of project scope: they pass the scope decorator on the
/// single GET, the details page and the scope of work, they see the project in the list and in
/// <c>mine=true</c>; once removed they get 404 again. A duplicate add is 409, an unknown employee 404, a
/// caller without <c>project.manage_members</c> 403.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProjectMemberRouteTests(ApiFixture fixture)
{
    [Fact]
    public async Task Member_add_duplicate_change_access_remove_and_member_scope()
    {
        string suffix = Guid.NewGuid().ToString("N");
        (_, string ownerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"pm-owner-{suffix}@example.com");
        (Guid memberEmployeeId, string memberUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"pm-member-{suffix}@example.com");

        HttpClient owner = Client(ownerUserId,
            "project.create", "project.read", "project.manage_members", "clients.create", "clients.read", "scope.read");
        Guid projectId = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, $"PM-{suffix}");

        HttpClient member = Client(memberUserId, "project.read", "scope.read");

        // Before: not a member, so 404 everywhere, and the list doesn't show it.
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/projects/{projectId}")).StatusCode);
        Assert.DoesNotContain(projectId, await ListIdsAsync(member, ""));

        // Route claim: 403 before anything else.
        HttpResponseMessage forbidden = await Client(ownerUserId, "project.read")
            .PostAsJsonAsync($"/api/projects/{projectId}/members", new { EmployeeId = memberEmployeeId, Access = 2 });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // Unknown employee: 404.
        HttpResponseMessage unknown = await owner.PostAsJsonAsync(
            $"/api/projects/{projectId}/members", new { EmployeeId = Guid.NewGuid(), Access = 2 });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        // Add.
        HttpResponseMessage added = await owner.PostAsJsonAsync(
            $"/api/projects/{projectId}/members", new { EmployeeId = memberEmployeeId, Access = 2 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Guid memberId = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("memberId").GetGuid();

        // Duplicate: 409.
        HttpResponseMessage duplicate = await owner.PostAsJsonAsync(
            $"/api/projects/{projectId}/members", new { EmployeeId = memberEmployeeId, Access = 1 });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("projects.members.AlreadyMember", await duplicate.Content.ReadAsStringAsync());

        // List shape.
        JsonElement[] members = await MembersAsync(owner, projectId);
        JsonElement row = Assert.Single(members);
        Assert.Equal(memberId, row.GetProperty("id").GetGuid());
        Assert.Equal(projectId, row.GetProperty("projectId").GetGuid());
        Assert.Equal(memberEmployeeId, row.GetProperty("employeeId").GetGuid());
        Assert.Equal(2, row.GetProperty("access").GetInt32());
        Assert.False(row.GetProperty("canManage").GetBoolean());
        Assert.True(row.GetProperty("canExecuteTasks").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("employeeName").GetString()));

        // Member scope: GET, details, scope of work, the list and mine=true all agree.
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/projects/{projectId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/projects/{projectId}/details")).StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, (await member.GetAsync($"/api/projects/{projectId}/scope-of-work")).StatusCode);
        Assert.Contains(projectId, await ListIdsAsync(member, ""));
        Assert.Contains(projectId, await ListIdsAsync(member, "&mine=true"));

        // Change access.
        Assert.Equal(HttpStatusCode.NoContent,
            (await owner.PutAsJsonAsync($"/api/projects/{projectId}/members/{memberId}/access", new { Access = 3 })).StatusCode);
        row = Assert.Single(await MembersAsync(owner, projectId));
        Assert.Equal(3, row.GetProperty("access").GetInt32());
        Assert.True(row.GetProperty("canManage").GetBoolean());

        // A member id from another project: 404, never a cross-project write.
        Guid otherProject = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, $"PM2-{suffix}");
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.DeleteAsync($"/api/projects/{otherProject}/members/{memberId}")).StatusCode);

        // Remove: the member is out of scope again.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/projects/{projectId}/members/{memberId}")).StatusCode);
        Assert.Empty(await MembersAsync(owner, projectId));
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/projects/{projectId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/projects/{projectId}/details")).StatusCode);
        Assert.DoesNotContain(projectId, await ListIdsAsync(member, "&mine=true"));
    }

    private static async Task<JsonElement[]> MembersAsync(HttpClient client, Guid projectId)
    {
        HttpResponseMessage response = await client.GetAsync($"/api/projects/{projectId}/members");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return [.. (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray()];
    }

    private static async Task<List<Guid>> ListIdsAsync(HttpClient client, string query)
    {
        HttpResponseMessage response = await client.GetAsync($"/api/projects?pageNumber=1&pageSize=200{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
        return [.. page.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetGuid())];
    }

    private HttpClient Client(string userId, params string[] permissions)
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(userId, permissions));
        return client;
    }
}
