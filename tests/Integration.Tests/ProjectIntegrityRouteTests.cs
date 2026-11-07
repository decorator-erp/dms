using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace Integration.Tests;

/// <summary>Sites/Projects review, phase 3 (Projects data integrity): the route-level red tests.</summary>
[Collection(ApiCollection.Name)]
public sealed class ProjectIntegrityRouteTests(ApiFixture fixture)
{
    private static readonly string[] OfficeCodes =
    [
        "project.create", "project.read", "project.update", "project.delete", "clients.create", "clients.read",
        "stages.read", "stages.manage", "stages.complete", "stages.manage_gates", "scope.read", "scope.manage",
        "scope.confirm", "stage_tasks.read", "stage_tasks.manage", "stage_tasks.complete",
        "documents.upload", "documents.delete", "documents.read", "sites.create", "sites.read",
        "crm.space_types.manage", "crm.spaces.manage", "crm.spaces.read",
        "finance.manage_cost_category", "finance.manage_budget",
    ];

    /// <summary>P12: deleting the stage a confirmed scope-of-work item created clears the item's
    /// <c>CreatedStageId</c> in the same save, so the scope never points at a stage that is gone.</summary>
    [Fact]
    public async Task P12_DeletingAStageClearsTheScopeItemThatCreatedIt()
    {
        (HttpClient owner, Guid projectId, Guid stageId) = await ConfirmedProjectAsync("p12");
        JsonElement item = Assert.Single((await owner.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/scope-of-work"))
            .GetProperty("items").EnumerateArray());
        Assert.Equal(stageId, item.GetProperty("createdStageId").GetGuid());

        HttpResponseMessage deleted = await owner.DeleteAsync($"/api/projects/{projectId}/stages/{stageId}");
        Assert.True(deleted.StatusCode == HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        item = Assert.Single((await owner.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/scope-of-work"))
            .GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("createdStageId").ValueKind);
    }

    /// <summary>P1: an empty project, and a project whose stage and tasks never started, delete (204) — and the
    /// stage, its tasks, sub-tasks and team go in the same transaction instead of being left orphaned.</summary>
    [Fact]
    public async Task P1_AnUntouchedProjectDeletes_WithItsStagesAndTasks()
    {
        (HttpClient owner, Guid projectId, Guid stageId) = await ConfirmedProjectAsync("p1-ok");
        await CreateTaskAsync(owner, projectId, stageId, "Never started");
        Assert.Equal(1, await CountAsync("projects.project_stages", projectId));
        Assert.Equal(1, await CountAsync("projects.project_tasks", projectId));

        HttpResponseMessage deleted = await owner.DeleteAsync($"/api/projects/{projectId}");
        Assert.True(deleted.StatusCode == HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/projects/{projectId}")).StatusCode);
        Assert.Equal(0, await CountAsync("projects.project_stages", projectId));
        Assert.Equal(0, await CountAsync("projects.project_tasks", projectId));

        Guid empty = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, $"P1E-{Guid.NewGuid().ToString("N")[..10]}");
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/projects/{empty}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/projects/{empty}")).StatusCode);
    }

    /// <summary>P1: each blocker is a 409 <c>projects.NotDeletable</c> and the project stays — a started task, a
    /// (soft-deleted) document, a (soft-deleted) site, a CRM space, a Finance budget line and a pending approval
    /// gate.</summary>
    [Fact]
    public async Task P1_EachBlocker_IsNotDeletable()
    {
        (HttpClient owner, Guid started, Guid startedStage) = await ConfirmedProjectAsync("p1-start");
        Guid task = await CreateTaskAsync(owner, started, startedStage, "Started");
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/projects/{started}/tasks/{task}/start", null)).StatusCode);
        await AssertNotDeletableAsync(owner, started);

        (_, Guid documented, _) = await ConfirmedProjectAsync("p1-doc", owner);
        using (var content = new MultipartFormDataContent())
        {
            var file = new ByteArrayContent(Encoding.UTF8.GetBytes("plan"));
            file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            content.Add(file, "file", "plan.txt");
            HttpResponseMessage uploaded = await owner.PostAsync($"/api/projects/{documented}/documents", content);
            Assert.True(uploaded.StatusCode == HttpStatusCode.OK, await uploaded.Content.ReadAsStringAsync());
            Guid documentId = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("documentId").GetGuid();
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/project-documents/{documentId}")).StatusCode);
        }

        await AssertNotDeletableAsync(owner, documented);

        (_, Guid surveyed, _) = await ConfirmedProjectAsync("p1-site", owner);
        HttpResponseMessage site = await owner.PostAsJsonAsync("/api/sites", new { ProjectId = surveyed, Data = new { Address = "1 Site Rd" } });
        Assert.True(site.StatusCode == HttpStatusCode.Created, await site.Content.ReadAsStringAsync());
        await ExecuteAsync("UPDATE sites.sites SET \"IsActive\" = false WHERE \"ProjectId\" = @id", surveyed);
        await AssertNotDeletableAsync(owner, surveyed);

        (_, Guid spaced, _) = await ConfirmedProjectAsync("p1-crm", owner);
        string code = $"P1{Guid.NewGuid().ToString("N")[..8]}";
        HttpResponseMessage spaceType = await owner.PostAsJsonAsync("/api/crm/space-types", new { Code = code, NameEn = "Room", NameAr = "غرفة" });
        Assert.True(spaceType.IsSuccessStatusCode, await spaceType.Content.ReadAsStringAsync());
        Guid spaceTypeId = (await spaceType.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("spaceTypeId").GetGuid();
        HttpResponseMessage space = await owner.PostAsJsonAsync($"/api/crm/projects/{spaced}/spaces", new { SpaceTypeId = spaceTypeId, Name = "Kitchen", SortOrder = 0 });
        Assert.True(space.IsSuccessStatusCode, await space.Content.ReadAsStringAsync());
        await AssertNotDeletableAsync(owner, spaced);

        (_, Guid budgeted, _) = await ConfirmedProjectAsync("p1-fin", owner);
        HttpResponseMessage category = await owner.PostAsJsonAsync("/api/cost-categories", new { Code = code, Name = "Materials" });
        Assert.True(category.IsSuccessStatusCode, await category.Content.ReadAsStringAsync());
        Guid categoryId = (await category.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("costCategoryId").GetGuid();
        HttpResponseMessage budget = await owner.PostAsJsonAsync("/api/budget-lines", new
        {
            Code = "BL-1",
            ProjectId = budgeted,
            CostCategoryId = categoryId,
            Name = "Budget",
            AllocatedAmount = 100m,
        });
        Assert.True(budget.IsSuccessStatusCode, await budget.Content.ReadAsStringAsync());
        await AssertNotDeletableAsync(owner, budgeted);

        (_, Guid gated, Guid gatedStage) = await ConfirmedProjectAsync("p1-gate", owner);
        HttpResponseMessage gate = await owner.PostAsJsonAsync($"/api/projects/{gated}/stages/{gatedStage}/gates", new
        {
            ConditionType = 3,
            Trigger = 2,
            Label = "Sign-off",
            ApprovalType = 1,
        });
        Assert.True(gate.StatusCode == HttpStatusCode.OK, await gate.Content.ReadAsStringAsync());
        await AssertNotDeletableAsync(owner, gated);
    }

    /// <summary>R1-2: one lock order, stage rows before task rows. A stage delete (the stage, then its tasks) and a
    /// task completion in that stage used to take the two rows in opposite orders and deadlock (40P01, a 500). A third
    /// transaction holds the stage so the delete queues on it first and the completion second; once it lets go, one
    /// wins and the other sees its result.</summary>
    [Fact]
    public async Task R1_2_DeletingAStageWhileCompletingOneOfItsTasks_DoesNotDeadlock()
    {
        (HttpClient owner, Guid projectId, Guid stageId) = await ConfirmedProjectAsync("r1-2");
        Guid task = await CreateTaskAsync(owner, projectId, stageId, "Racing");
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/projects/{projectId}/tasks/{task}/start", null)).StatusCode);

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlTransaction tx = await connection.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand(
            "SELECT 1 FROM projects.project_stages WHERE \"Id\" = @id FOR NO KEY UPDATE", connection, tx))
        {
            hold.Parameters.AddWithValue("id", stageId);
            await hold.ExecuteNonQueryAsync();
        }

        Task<HttpResponseMessage> delete = owner.DeleteAsync($"/api/projects/{projectId}/stages/{stageId}");
        await Task.Delay(500);
        Task<HttpResponseMessage> complete = owner.PostAsync($"/api/projects/{projectId}/tasks/{task}/complete", null);
        await Task.Delay(500);
        await tx.CommitAsync();

        HttpResponseMessage deleted = await delete;
        HttpResponseMessage completed = await complete;

        // The task had started, so the delete is refused and the completion goes through.
        Assert.True(deleted.StatusCode == HttpStatusCode.Conflict, $"{deleted.StatusCode}: {await deleted.Content.ReadAsStringAsync()}");
        Assert.True(completed.StatusCode == HttpStatusCode.NoContent, $"{completed.StatusCode}: {await completed.Content.ReadAsStringAsync()}");
    }

    private static async Task AssertNotDeletableAsync(HttpClient owner, Guid projectId)
    {
        HttpResponseMessage refused = await owner.DeleteAsync($"/api/projects/{projectId}");
        string body = await refused.Content.ReadAsStringAsync();
        Assert.True(refused.StatusCode == HttpStatusCode.Conflict, $"{refused.StatusCode}: {body}");
        Assert.Contains("projects.NotDeletable", body);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/projects/{projectId}")).StatusCode);
    }

    private static async Task<Guid> CreateTaskAsync(HttpClient owner, Guid projectId, Guid stageId, string name)
    {
        HttpResponseMessage response = await owner.PostAsJsonAsync($"/api/projects/{projectId}/tasks", new { ProjectStageId = stageId, Name = name });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("taskId").GetGuid();
    }

    private async Task<long> CountAsync(string table, Guid projectId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE \"ProjectId\" = @id", connection);
        command.Parameters.AddWithValue("id", projectId);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExecuteAsync(string sql, Guid projectId)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", projectId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<(HttpClient Owner, Guid ProjectId, Guid StageId)> ConfirmedProjectAsync(string label, HttpClient? existing = null)
    {
        string suffix = Guid.NewGuid().ToString("N")[..10];
        HttpClient owner = existing ?? Client((await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"{label}-owner-{suffix}@example.com")).AppUserId, OfficeCodes);
        (Guid projectId, Guid stageId, _) = await ProjectTestSeeding.CreateConfirmedStageProjectAsync(owner, $"{label.ToUpperInvariant()}-{suffix}");
        return (owner, projectId, stageId);
    }

    private HttpClient Client(string userId, params string[] permissions)
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(userId, permissions));
        return client;
    }
}
