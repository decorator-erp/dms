using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace Integration.Tests;

/// <summary>PR E review loop 1: one test per finding fixed red before green (FINDING-E-1 … E-6).</summary>
[Collection(ApiCollection.Name)]
public sealed class ProjectReviewLoopETests(ApiFixture fixture)
{
    private static readonly string[] OfficeCodes =
    [
        "project.create", "project.read", "clients.create", "clients.read", "stages.read", "stages.manage",
        "stages.complete", "stages.manage_gates", "scope.read", "scope.manage", "scope.confirm", "stage_tasks.read",
        "stage_tasks.manage", "stage_tasks.complete", "documents.upload", "documents.delete", "documents.read",
        "project.manage_members",
    ];

    /// <summary>E-1: a soft-deleted document (and the task's default folder) still references the task, so the
    /// delete is 409 NotDeletable, never a 500 from the cross-schema FK.</summary>
    [Fact]
    public async Task E1_task_with_a_deleted_document_is_not_deletable_409_not_500()
    {
        (HttpClient owner, Guid projectId, Guid stageId) = await ConfirmedProjectAsync("e1");
        Guid taskId = await CreateTaskAsync(owner, projectId, stageId, "Docs");

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("plan"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(file, "file", "plan.txt");
        HttpResponseMessage uploaded = await owner.PostAsync($"/api/project-tasks/{taskId}/documents", content);
        Assert.True(uploaded.StatusCode == HttpStatusCode.OK, await uploaded.Content.ReadAsStringAsync());
        Guid documentId = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/project-documents/{documentId}")).StatusCode);

        HttpResponseMessage task = await owner.DeleteAsync($"/api/projects/{projectId}/tasks/{taskId}");
        Assert.Equal(HttpStatusCode.Conflict, task.StatusCode);
        HttpResponseMessage stage = await owner.DeleteAsync($"/api/projects/{projectId}/stages/{stageId}");
        Assert.Equal(HttpStatusCode.Conflict, stage.StatusCode);
    }

    /// <summary>E-2: a task added to a locked stage starts Open, like a materialised stage's tasks, so it can be
    /// started once the stage's Unlock gate is satisfied (gate rules unchanged).</summary>
    [Fact]
    public async Task E2_task_added_to_a_locked_stage_starts_open()
    {
        (HttpClient owner, Guid projectId, Guid stageId) = await ConfirmedProjectAsync("e2");
        HttpResponseMessage gate = await owner.PostAsJsonAsync($"/api/projects/{projectId}/stages/{stageId}/gates",
            new { ConditionType = 4, Trigger = 1, Label = "Unlock me" });
        Assert.Equal(HttpStatusCode.OK, gate.StatusCode);
        Assert.Equal(1, (await owner.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/stages/{stageId}")).GetProperty("status").GetInt32());

        Guid taskId = await CreateTaskAsync(owner, projectId, stageId, "During lock");
        Assert.Equal(2, (await owner.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/tasks/{taskId}")).GetProperty("status").GetInt32());
    }

    /// <summary>E-3: a comment racing a task delete. While the delete holds the task row (simulated with a
    /// FOR UPDATE in a second connection, then the row deleted), the comment waits and then finds no task (404)
    /// instead of writing an orphan.</summary>
    [Fact]
    public async Task E3_comment_racing_a_task_delete_waits_and_gets_404_no_orphan()
    {
        (HttpClient owner, Guid projectId, Guid stageId) = await ConfirmedProjectAsync("e3");
        Guid taskId = await CreateTaskAsync(owner, projectId, stageId, "Racy");

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlTransaction tx = await connection.BeginTransactionAsync();
        await using (var lockTask = new NpgsqlCommand("SELECT 1 FROM projects.project_tasks WHERE \"Id\" = @id FOR UPDATE", connection, tx))
        {
            lockTask.Parameters.AddWithValue("id", taskId);
            await lockTask.ExecuteScalarAsync();
        }

        Task<HttpResponseMessage> comment = owner.PostAsJsonAsync($"/api/projects/{projectId}/tasks/{taskId}/comments", new { Content = "late" });
        await Task.WhenAny(comment, Task.Delay(1500));
        Assert.False(comment.IsCompleted, "the comment must wait for the task row");

        await using (var delete = new NpgsqlCommand("DELETE FROM projects.project_tasks WHERE \"Id\" = @id", connection, tx))
        {
            delete.Parameters.AddWithValue("id", taskId);
            await delete.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await comment).StatusCode);

        await using var count = new NpgsqlCommand("SELECT count(*) FROM projects.task_comments WHERE \"TaskId\" = @id", connection);
        count.Parameters.AddWithValue("id", taskId);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
    }

    /// <summary>E-4: a completed stage is not deletable even with no tasks.</summary>
    [Fact]
    public async Task E4_completed_stage_is_not_deletable()
    {
        (HttpClient owner, Guid projectId, _) = await ConfirmedProjectAsync("e4");
        HttpResponseMessage created = await owner.PostAsJsonAsync($"/api/projects/{projectId}/stages", new { Name = "Quick", Order = 2 });
        Guid quick = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("stageId").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/projects/{projectId}/stages/{quick}/complete", null)).StatusCode);

        HttpResponseMessage refused = await owner.DeleteAsync($"/api/projects/{projectId}/stages/{quick}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("projects.stages.NotDeletable", await refused.Content.ReadAsStringAsync());
    }

    /// <summary>E-5: a PUT that leaves a field out keeps its value — the stage's order, the task's priority and
    /// weight, the sub-task's order and required flag.</summary>
    [Fact]
    public async Task E5_partial_put_keeps_the_fields_it_leaves_out()
    {
        (HttpClient owner, Guid projectId, Guid designId) = await ConfirmedProjectAsync("e5");
        HttpResponseMessage second = await owner.PostAsJsonAsync($"/api/projects/{projectId}/stages", new { Name = "Second", Order = 2 });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/api/projects/{projectId}/stages/{designId}", new { Name = "Design renamed" })).StatusCode);
        Assert.Equal(1, (await owner.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/stages/{designId}")).GetProperty("order").GetInt32());

        HttpResponseMessage task = await owner.PostAsJsonAsync($"/api/projects/{projectId}/tasks",
            new { ProjectStageId = designId, Name = "Weighted", Priority = 4, Weight = 3m, Order = 7 });
        Guid taskId = (await task.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("taskId").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/api/projects/{projectId}/tasks/{taskId}", new { Name = "Weighted 2" })).StatusCode);
        JsonElement t = await owner.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/tasks/{taskId}");
        Assert.Equal(4, t.GetProperty("priority").GetInt32());
        Assert.Equal(3m, t.GetProperty("weight").GetDecimal());
        Assert.Equal(7, t.GetProperty("order").GetInt32());

        string subTasks = $"/api/projects/{projectId}/tasks/{taskId}/sub-tasks";
        HttpResponseMessage sub = await owner.PostAsJsonAsync(subTasks, new { Title = "Optional", Order = 4, IsRequired = false });
        Guid subId = (await sub.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("subTaskId").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"{subTasks}/{subId}", new { Title = "Optional 2" })).StatusCode);
        JsonElement s = (await owner.GetFromJsonAsync<JsonElement>(subTasks)).EnumerateArray().Single();
        Assert.False(s.GetProperty("isRequired").GetBoolean());
        Assert.Equal(4, s.GetProperty("order").GetInt32());
    }

    /// <summary>E-6: a read-all caller adding a member to, or listing the members of, a project that doesn't
    /// exist gets 404, not 500 / an empty 200.</summary>
    [Fact]
    public async Task E6_members_of_an_unknown_project_are_404_for_a_read_all_caller()
    {
        (_, string userId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"e6-{Guid.NewGuid():N}@example.com");
        (Guid employeeId, _) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"e6m-{Guid.NewGuid():N}@example.com");
        HttpClient admin = Client(userId, "project.read", "project.read_all", "project.manage_members");
        var unknown = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PostAsJsonAsync($"/api/projects/{unknown}/members", new { EmployeeId = employeeId, Access = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/projects/{unknown}/members")).StatusCode);
    }

    private async Task<(HttpClient Owner, Guid ProjectId, Guid StageId)> ConfirmedProjectAsync(string label)
    {
        string suffix = Guid.NewGuid().ToString("N")[..10];
        (_, string ownerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"{label}-owner-{suffix}@example.com");
        HttpClient owner = Client(ownerUserId, OfficeCodes);
        (Guid projectId, Guid stageId, _) = await ProjectTestSeeding.CreateConfirmedStageProjectAsync(owner, $"{label.ToUpperInvariant()}-{suffix}");
        return (owner, projectId, stageId);
    }

    private static async Task<Guid> CreateTaskAsync(HttpClient owner, Guid projectId, Guid stageId, string name)
    {
        HttpResponseMessage response = await owner.PostAsJsonAsync($"/api/projects/{projectId}/tasks", new { ProjectStageId = stageId, Name = name });
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
