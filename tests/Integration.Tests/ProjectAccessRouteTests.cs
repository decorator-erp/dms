using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>Sites/Projects phase 5: what the employee role's baseline (v5) codes do over HTTP, with the project level
/// deciding per project (P10). The assignee (Editor) starts, comments on and completes their task; a Viewer member
/// holding the same codes is 403 at the level check; planning a task stays an office claim.</summary>
[Collection(ApiCollection.Name)]
public sealed class ProjectAccessRouteTests(ApiFixture fixture)
{
    private static readonly string[] OfficeCodes =
    [
        "project.create", "project.read", "clients.create", "clients.read", "stage_tasks.read", "stage_tasks.manage",
        "project.manage_members",
    ];

    /// <summary>The employee codes <c>baseline-claims-v5</c> grants (with v1's own two).</summary>
    private static readonly string[] EmployeeCodes =
    [
        "employees.view_own", "notifications.view_own", "project.read", "stages.read", "scope.read", "stage_tasks.read",
        "stage_tasks.complete", "sites.read", "sites.create", "sites.update", "sites.submit",
    ];

    [Fact]
    public async Task BaselineV5_TheAssigneeExecutesTheirTask_AViewerIsRefusedAtTheLevel()
    {
        string suffix = Guid.NewGuid().ToString("N")[..10];
        (_, string ownerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"pa-owner-{suffix}@example.com");
        (Guid assigneeId, string assigneeUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"pa-assignee-{suffix}@example.com");
        (Guid viewerId, string viewerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"pa-viewer-{suffix}@example.com");
        HttpClient owner = Client(ownerUserId, OfficeCodes);

        Guid projectId = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, $"PA-{suffix}");
        string tasks = $"/api/projects/{projectId}/tasks";
        Guid taskId = await CreateTaskAsync(owner, tasks, "Measure", assigneeId);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/projects/{projectId}/members", new { EmployeeId = viewerId, Access = 1 })).StatusCode);

        HttpClient assignee = Client(assigneeUserId, EmployeeCodes);
        HttpClient viewer = Client(viewerUserId, EmployeeCodes);

        Assert.Equal(HttpStatusCode.OK, (await assignee.GetAsync($"/api/projects/{projectId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"/api/projects/{projectId}")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"{tasks}/{taskId}/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await assignee.PostAsync($"{tasks}/{taskId}/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"{tasks}/{taskId}/comments", new { Content = "Seen" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await assignee.PostAsJsonAsync($"{tasks}/{taskId}/comments", new { Content = "On it" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await assignee.PostAsync($"{tasks}/{taskId}/complete", null)).StatusCode);

        // Planning stays an office claim: the route refuses it before any level is read.
        Assert.Equal(HttpStatusCode.Forbidden, (await assignee.PostAsJsonAsync(tasks, new { Name = "Extra" })).StatusCode);
    }

    private static async Task<Guid> CreateTaskAsync(HttpClient owner, string route, string name, Guid assigneeId)
    {
        HttpResponseMessage response = await owner.PostAsJsonAsync(route, new { Name = name, AssignedToId = assigneeId });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("taskId").GetGuid();
    }

    private HttpClient Client(string userId, params string[] permissions)
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(userId, permissions));
        return client;
    }
}
