using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>E7: stage teams. A team made by hand (a standalone head, or a department's head), the project's
/// team list with head and member count (filterable by stage), and member removal, after which the removed
/// employee is out of project scope again.</summary>
[Collection(ApiCollection.Name)]
public sealed class ProjectTeamRouteTests(ApiFixture fixture)
{
    [Fact]
    public async Task Team_create_list_with_counts_and_member_removal_drops_scope()
    {
        string suffix = Guid.NewGuid().ToString("N")[..10];
        (_, string ownerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"tm-owner-{suffix}@example.com");
        (Guid headId, _) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"tm-head-{suffix}@example.com");
        (Guid workerId, string workerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"tm-worker-{suffix}@example.com");
        HttpClient owner = Client(ownerUserId,
            "project.create", "project.read", "clients.create", "clients.read", "stages.read", "stages.manage",
            "scope.read", "scope.manage", "scope.confirm", "stage_teams.manage");

        (Guid projectId, Guid stageId, _) = await ProjectTestSeeding.CreateConfirmedStageProjectAsync(owner, $"TM-{suffix}");
        string teams = $"/api/projects/{projectId}/teams";

        // 403, 400 (neither head nor department), 409 (a department with no head), 404 (stage of another project).
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(ownerUserId, "stages.read").PostAsJsonAsync(teams, new { ProjectStageId = stageId, HeadId = headId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(teams, new { ProjectStageId = stageId })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(teams, new { ProjectStageId = stageId, DepartmentId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync(teams, new { ProjectStageId = Guid.NewGuid(), HeadId = headId })).StatusCode);

        HttpResponseMessage created = await owner.PostAsJsonAsync(teams, new { ProjectStageId = stageId, HeadId = headId });
        Assert.True(created.StatusCode == HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        Guid teamId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("stageTeamId").GetGuid();

        HttpResponseMessage added = await owner.PostAsJsonAsync($"{teams}/{teamId}/members", new { EmployeeId = workerId, Role = 2 });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Guid memberId = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("stageTeamMemberId").GetGuid();

        JsonElement row = Assert.Single((await owner.GetFromJsonAsync<JsonElement>(teams)).EnumerateArray());
        Assert.Equal(teamId, row.GetProperty("id").GetGuid());
        Assert.Equal(stageId, row.GetProperty("projectStageId").GetGuid());
        Assert.Equal("Design", row.GetProperty("stageName").GetString());
        Assert.Equal(headId, row.GetProperty("headId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("headName").GetString()));
        Assert.Equal(1, row.GetProperty("memberCount").GetInt32());
        Assert.Single((await owner.GetFromJsonAsync<JsonElement>($"{teams}?stageId={stageId}")).EnumerateArray());
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>($"{teams}?stageId={Guid.NewGuid()}")).EnumerateArray());

        // The worker is in scope through the team; removing them takes them out.
        HttpClient worker = Client(workerUserId, "project.read");
        Assert.Equal(HttpStatusCode.OK, (await worker.GetAsync($"/api/projects/{projectId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{teams}/members/{memberId}")).StatusCode);
        Assert.Equal(0, Assert.Single((await owner.GetFromJsonAsync<JsonElement>(teams)).EnumerateArray()).GetProperty("memberCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await worker.GetAsync($"/api/projects/{projectId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"{teams}/members/{memberId}")).StatusCode);
    }

    private HttpClient Client(string userId, params string[] permissions)
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(userId, permissions));
        return client;
    }
}
