using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>F3 (D5): My Work. One row per open project task assigned to the calling employee that has gathering
/// work: its stage's brief and status, its brief-space entries and requirement items (total and pending), the
/// unresolved count, the project's open period close time and upcoming meetings. Someone else's tasks never
/// show; shape per the frontend's <c>MyGatheringWorkItem</c>.</summary>
[Collection(ApiCollection.Name)]
public sealed class CrmMyGatheringWorkRouteTests(ApiFixture fixture)
{
    [Fact]
    public async Task Assignee_sees_their_task_block_others_see_nothing()
    {
        PortalWorld w = await CrmPortalTestSeeding.CreateAsync(fixture, "f3");
        HttpClient office = CrmPortalTestSeeding.Client(fixture, w.OfficeUserId,
            [.. CrmPortalTestSeeding.OfficeCodes, "stage_tasks.manage", "stage_tasks.read"]);
        HttpResponseMessage task = await office.PostAsJsonAsync($"/api/projects/{w.ProjectId}/tasks",
            new { ProjectStageId = w.StageId, Name = "Gather requirements", AssignedToId = w.OfficeEmployeeId });
        Assert.True(task.StatusCode == HttpStatusCode.OK, await task.Content.ReadAsStringAsync());
        Guid taskId = (await task.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("taskId").GetGuid();

        HttpResponseMessage brief = await office.PostAsJsonAsync($"/api/crm/projects/{w.ProjectId}/brief",
            new { ProjectStageId = w.StageId, Data = new { UnitType = 1, UnitCondition = 1, Address = "F3 Addr" } });
        Assert.Equal(HttpStatusCode.Created, brief.StatusCode);
        Guid briefId = (await brief.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("briefId").GetGuid();

        HttpResponseMessage item = await office.PostAsJsonAsync($"/api/crm/periods/{w.PeriodId}/items",
            new { w.ProjectId, Category = 1, Title = "Task item", Priority = 2, ProjectTaskId = taskId });
        Guid itemId = (await item.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("itemId").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await office.PutAsync($"/api/crm/requirement-items/{itemId}/send", null)).StatusCode);

        const string route = "/api/crm/my-gathering-work";
        Assert.Equal(HttpStatusCode.Forbidden, (await CrmPortalTestSeeding.Client(fixture, w.OfficeUserId, "project.read").GetAsync(route)).StatusCode);

        JsonElement rows = await CrmPortalTestSeeding.Client(fixture, w.OfficeUserId, "crm.briefs.read").GetFromJsonAsync<JsonElement>(route);
        JsonElement row = Assert.Single(rows.EnumerateArray(), r => r.GetProperty("projectTaskId").GetGuid() == taskId);
        Assert.Equal("Gather requirements", row.GetProperty("taskName").GetString());
        Assert.Equal(w.ProjectId, row.GetProperty("projectId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(row.GetProperty("projectName").GetString()));
        Assert.Equal(w.StageId, row.GetProperty("projectStageId").GetGuid());
        Assert.Equal("Design", row.GetProperty("stageName").GetString());
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("openPeriodClosesAtUtc").ValueKind);
        JsonElement block = row.GetProperty("taskBlock");
        Assert.Equal(briefId, block.GetProperty("briefId").GetGuid());
        Assert.Equal(1, block.GetProperty("briefStatus").GetInt32()); // Draft
        Assert.Equal(1, block.GetProperty("itemTotal").GetInt32());
        Assert.Equal(1, block.GetProperty("itemPending").GetInt32());
        Assert.Equal(1, block.GetProperty("unresolvedTotal").GetInt32());
        Assert.Equal(0, block.GetProperty("entryTotal").GetInt32());

        (_, string otherUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"f3-other-{Guid.NewGuid():N}@example.com");
        Assert.Empty((await CrmPortalTestSeeding.Client(fixture, otherUserId, "crm.briefs.read").GetFromJsonAsync<JsonElement>(route)).EnumerateArray());
    }
}
