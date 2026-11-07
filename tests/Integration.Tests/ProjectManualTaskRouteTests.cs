using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// E3 (D2): manual tasks. Create (stage project after confirm, or FreeStyle with no stage), update, the list
/// filters, the one-shot details (sub-task count, comment count, gate summary) and the delete rule: only a
/// never-started task with no comments or documents. Route claim 403 → scope 404 → state 409.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProjectManualTaskRouteTests(ApiFixture fixture)
{
    private static readonly string[] OfficeCodes =
    [
        "project.create", "project.read", "clients.create", "clients.read", "stages.read", "stages.manage",
        "scope.read", "scope.manage", "scope.confirm", "stage_tasks.read", "stage_tasks.manage",
    ];

    [Fact]
    public async Task Manual_task_create_filters_details_update_and_delete_rule()
    {
        string suffix = Guid.NewGuid().ToString("N")[..10];
        (_, string ownerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"mt-owner-{suffix}@example.com");
        (Guid assigneeId, _) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"mt-assignee-{suffix}@example.com");
        (_, string outsiderUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"mt-out-{suffix}@example.com");
        HttpClient owner = Client(ownerUserId, OfficeCodes);

        (Guid projectId, Guid designId, _) = await ProjectTestSeeding.CreateConfirmedStageProjectAsync(owner, $"MTC-{suffix}");
        object body = new
        {
            ProjectStageId = designId,
            Name = "Measure site",
            Description = "Laser measure",
            AssignedToId = assigneeId,
            Priority = 3,
            DueDate = "2026-12-15",
        };

        // 403, 404, 400.
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(ownerUserId, "stage_tasks.read")
            .PostAsJsonAsync($"/api/projects/{projectId}/tasks", body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client(outsiderUserId, "stage_tasks.manage")
            .PostAsJsonAsync($"/api/projects/{projectId}/tasks", body)).StatusCode);
        HttpResponseMessage noStage = await owner.PostAsJsonAsync($"/api/projects/{projectId}/tasks", new { Name = "No stage" });
        Assert.Equal(HttpStatusCode.BadRequest, noStage.StatusCode);
        Assert.Contains("tasks.StageRequired", await noStage.Content.ReadAsStringAsync());

        // Before confirm the project has no stage, so any stage id is missing: R1-3 puts that 404 before the 409s.
        (Guid draftProject, _) = await ProjectTestSeeding.CreateDraftStageProjectAsync(owner, $"MTD-{suffix}");
        HttpResponseMessage early = await owner.PostAsJsonAsync($"/api/projects/{draftProject}/tasks", new { ProjectStageId = Guid.NewGuid(), Name = "x" });
        Assert.Equal(HttpStatusCode.NotFound, early.StatusCode);

        // FreeStyle: a task with no stage is fine; one with a stage is refused.
        Guid freeStyle = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, $"MTF-{suffix}");
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/projects/{freeStyle}/tasks", new { Name = "Loose task" })).StatusCode);

        // Create.
        HttpResponseMessage created = await owner.PostAsJsonAsync($"/api/projects/{projectId}/tasks", body);
        Assert.True(created.StatusCode == HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        Guid taskId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("taskId").GetGuid();
        JsonElement task = await owner.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/tasks/{taskId}");
        Assert.Equal("Measure site", task.GetProperty("name").GetString());
        Assert.Equal(assigneeId, task.GetProperty("assignedToId").GetGuid());
        Assert.Equal(2, task.GetProperty("status").GetInt32()); // Open
        Assert.Equal(1, await StageTotalTasksAsync(owner, projectId, designId));

        // Filters.
        Assert.Contains(taskId, await TaskIdsAsync(owner, projectId, $"?stageId={designId}"));
        Assert.Contains(taskId, await TaskIdsAsync(owner, projectId, $"?assigneeId={assigneeId}"));
        Assert.Contains(taskId, await TaskIdsAsync(owner, projectId, "?status=2"));
        Assert.DoesNotContain(taskId, await TaskIdsAsync(owner, projectId, "?status=3"));
        Assert.DoesNotContain(taskId, await TaskIdsAsync(owner, projectId, $"?assigneeId={Guid.NewGuid()}"));

        // Update.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/api/projects/{projectId}/tasks/{taskId}", new
        {
            Name = "Measure site again",
            Description = "Laser",
            Priority = 4,
            Weight = 2m,
            Order = 5,
            DueDate = "2026-12-20",
        })).StatusCode);
        task = await owner.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/tasks/{taskId}");
        Assert.Equal("Measure site again", task.GetProperty("name").GetString());
        Assert.Equal(4, task.GetProperty("priority").GetInt32());

        // Details: counts and gate summary.
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/projects/{projectId}/tasks/{taskId}/comments", new { Content = "On it" })).StatusCode);
        JsonElement details = await owner.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/tasks/{taskId}/details");
        Assert.Equal(taskId, details.GetProperty("task").GetProperty("id").GetGuid());
        Assert.Equal(0, details.GetProperty("subTasksCount").GetInt32());
        Assert.Equal(1, details.GetProperty("commentsCount").GetInt32());
        Assert.Equal(0, details.GetProperty("gates").GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Array, details.GetProperty("subTasks").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/projects/{projectId}/tasks/{Guid.NewGuid()}/details")).StatusCode);

        // Delete: a commented task is refused; a started one too; a fresh one goes.
        HttpResponseMessage commented = await owner.DeleteAsync($"/api/projects/{projectId}/tasks/{taskId}");
        Assert.Equal(HttpStatusCode.Conflict, commented.StatusCode);
        Assert.Contains("projects.tasks.NotDeletable", await commented.Content.ReadAsStringAsync());

        Guid started = await CreateAsync(owner, projectId, designId, "Started");
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/projects/{projectId}/tasks/{started}/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"/api/projects/{projectId}/tasks/{started}")).StatusCode);

        Guid fresh = await CreateAsync(owner, projectId, designId, "Fresh");
        Assert.Equal(3, await StageTotalTasksAsync(owner, projectId, designId));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/projects/{projectId}/tasks/{fresh}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/projects/{projectId}/tasks/{fresh}")).StatusCode);
        Assert.Equal(2, await StageTotalTasksAsync(owner, projectId, designId));
    }

    private static async Task<Guid> CreateAsync(HttpClient owner, Guid projectId, Guid stageId, string name)
    {
        HttpResponseMessage response = await owner.PostAsJsonAsync($"/api/projects/{projectId}/tasks", new { ProjectStageId = stageId, Name = name });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("taskId").GetGuid();
    }

    private static async Task<int> StageTotalTasksAsync(HttpClient client, Guid projectId, Guid stageId) =>
        (await client.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/stages/{stageId}")).GetProperty("totalTasks").GetInt32();

    private static async Task<List<Guid>> TaskIdsAsync(HttpClient client, Guid projectId, string query)
    {
        JsonElement tasks = await client.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/tasks{query}");
        return [.. tasks.EnumerateArray().Select(t => t.GetProperty("id").GetGuid())];
    }

    private HttpClient Client(string userId, params string[] permissions)
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(userId, permissions));
        return client;
    }
}
