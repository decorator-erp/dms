using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// E5: sub-tasks. Create, update and delete need <c>stage_tasks.manage</c>; delete only before the sub-task is
/// done (409). Complete and reopen are open to the task's assignee (with <c>stage_tasks.complete</c>) or a
/// manager; anyone else is 403 after the scope check. The task's sub-task counters follow every write, so a
/// reopened sub-task blocks the task's completion again.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProjectSubTaskRouteTests(ApiFixture fixture)
{
    private static readonly string[] OfficeCodes =
    [
        "project.create", "project.read", "clients.create", "clients.read", "stage_tasks.read", "stage_tasks.manage",
        "stage_tasks.complete", "project.manage_members",
    ];

    [Fact]
    public async Task Sub_task_crud_reopen_by_assignee_and_delete_rule()
    {
        string suffix = Guid.NewGuid().ToString("N")[..10];
        (_, string ownerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"st-owner-{suffix}@example.com");
        (Guid assigneeId, string assigneeUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"st-assignee-{suffix}@example.com");
        (Guid otherId, string otherUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"st-other-{suffix}@example.com");
        HttpClient owner = Client(ownerUserId, OfficeCodes);

        Guid projectId = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, $"ST-{suffix}");
        HttpResponseMessage taskResponse = await owner.PostAsJsonAsync($"/api/projects/{projectId}/tasks", new { Name = "Paint", AssignedToId = assigneeId });
        Assert.Equal(HttpStatusCode.OK, taskResponse.StatusCode);
        Guid taskId = (await taskResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("taskId").GetGuid();
        string subTasks = $"/api/projects/{projectId}/tasks/{taskId}/sub-tasks";

        // Both helpers are project members (so they pass scope); only one is the assignee.
        foreach (Guid employee in new[] { assigneeId, otherId })
        {
            Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/projects/{projectId}/members", new { EmployeeId = employee, Access = 2 })).StatusCode);
        }

        // 403 without the claim, 400 on a bad body.
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(ownerUserId, "stage_tasks.read").PostAsJsonAsync(subTasks, new { Title = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(subTasks, new { Title = "" })).StatusCode);

        Guid first = await CreateAsync(owner, subTasks, "Primer", 1);
        Guid second = await CreateAsync(owner, subTasks, "Top coat", 2);
        Assert.Equal(["Primer", "Top coat"], await TitlesAsync(owner, subTasks));

        // Update.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"{subTasks}/{second}",
            new { Title = "Top coat x2", Order = 2, IsRequired = false, Description = "two coats" })).StatusCode);
        JsonElement updated = (await owner.GetFromJsonAsync<JsonElement>(subTasks)).EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == second);
        Assert.Equal("Top coat x2", updated.GetProperty("title").GetString());
        Assert.False(updated.GetProperty("isRequired").GetBoolean());

        // Complete the first (the existing route), then a stranger to the task can't reopen it; the assignee can.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/projects/{projectId}/tasks/sub-tasks/{first}/complete", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(otherUserId, "stage_tasks.complete").PostAsync($"{subTasks}/{first}/reopen", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Client(assigneeUserId, "stage_tasks.complete").PostAsync($"{subTasks}/{first}/reopen", null)).StatusCode);
        Assert.False((await owner.GetFromJsonAsync<JsonElement>(subTasks)).EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == first).GetProperty("isDone").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync($"{subTasks}/{first}/reopen", null)).StatusCode); // not done

        // Counters: done 0 of 2, so the started task can't complete until both are done.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/projects/{projectId}/tasks/{taskId}/start", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync($"/api/projects/{projectId}/tasks/{taskId}/complete", null)).StatusCode);

        // Delete: a done sub-task is refused; an open one goes.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/projects/{projectId}/tasks/sub-tasks/{first}/complete", null)).StatusCode);
        HttpResponseMessage refused = await owner.DeleteAsync($"{subTasks}/{first}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("projects.subtasks.NotDeletable", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{subTasks}/{second}")).StatusCode);
        Assert.Equal(["Primer"], await TitlesAsync(owner, subTasks));

        // With the open one gone, 1 of 1 done: the task completes.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/projects/{projectId}/tasks/{taskId}/complete", null)).StatusCode);

        // A sub-task id under another task is 404.
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"/api/projects/{projectId}/tasks/{Guid.NewGuid()}/sub-tasks/{first}")).StatusCode);
    }

    private static async Task<Guid> CreateAsync(HttpClient owner, string route, string title, int order)
    {
        HttpResponseMessage response = await owner.PostAsJsonAsync(route, new { Title = title, Order = order, IsRequired = true });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("subTaskId").GetGuid();
    }

    private static async Task<string[]> TitlesAsync(HttpClient client, string route) =>
        [.. (await client.GetFromJsonAsync<JsonElement>(route)).EnumerateArray().Select(s => s.GetProperty("title").GetString()!)];

    private HttpClient Client(string userId, params string[] permissions)
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(userId, permissions));
        return client;
    }
}
