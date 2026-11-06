using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// E2 (D2): manual stages. Allowed on a Stage-workflow project once its scope of work is confirmed; 409
/// before that and on a FreeStyle project. Order stays contiguous (1..n) through create, update and delete.
/// Delete is refused (409 <c>projects.stages.NotDeletable</c>) once the stage has a satisfied gate.
/// Route claim 403 → scope 404 → state 409.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProjectManualStageRouteTests(ApiFixture fixture)
{
    private static readonly string[] OfficeCodes =
    [
        "project.create", "project.read", "clients.create", "clients.read", "stages.read", "stages.manage",
        "stages.manage_gates", "scope.read", "scope.manage", "scope.confirm",
    ];

    [Fact]
    public async Task Manual_stage_confirm_rule_contiguous_order_and_delete_refusal()
    {
        string suffix = Guid.NewGuid().ToString("N")[..10];
        (_, string ownerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"ms-owner-{suffix}@example.com");
        (_, string outsiderUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"ms-out-{suffix}@example.com");
        HttpClient owner = Client(ownerUserId, OfficeCodes);

        // Before confirm: 409. FreeStyle: 409.
        (Guid draftProject, _) = await ProjectTestSeeding.CreateDraftStageProjectAsync(owner, $"MSD-{suffix}");
        HttpResponseMessage early = await owner.PostAsJsonAsync($"/api/projects/{draftProject}/stages", new { Name = "Early", Order = 1 });
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Contains("projects.stages.ScopeNotConfirmed", await early.Content.ReadAsStringAsync());

        Guid freeStyle = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, $"MSF-{suffix}");
        HttpResponseMessage free = await owner.PostAsJsonAsync($"/api/projects/{freeStyle}/stages", new { Name = "Free", Order = 1 });
        Assert.Equal(HttpStatusCode.Conflict, free.StatusCode);
        Assert.Contains("projects.stages.WorkflowNotSupported", await free.Content.ReadAsStringAsync());

        (Guid projectId, Guid designId, _) = await ProjectTestSeeding.CreateConfirmedStageProjectAsync(owner, $"MSC-{suffix}");

        // 403 without the claim, 404 outside the project, 400 on a bad body.
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(ownerUserId, "stages.read")
            .PostAsJsonAsync($"/api/projects/{projectId}/stages", new { Name = "X", Order = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client(outsiderUserId, "stages.manage")
            .PostAsJsonAsync($"/api/projects/{projectId}/stages", new { Name = "X", Order = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner
            .PostAsJsonAsync($"/api/projects/{projectId}/stages", new { Name = "", Order = 1 })).StatusCode);

        // Create at position 1: Design moves to 2. Dates come back UTC.
        HttpResponseMessage created = await owner.PostAsJsonAsync($"/api/projects/{projectId}/stages", new
        {
            Name = "Survey",
            Order = 1,
            Description = "Site survey",
            EstimatedStartDate = "2026-12-01",
            EstimatedEndDate = "2026-12-10",
        });
        Assert.True(created.StatusCode == HttpStatusCode.OK, await created.Content.ReadAsStringAsync());
        Guid surveyId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("stageId").GetGuid();
        Assert.Equal(["Survey", "Design"], await NamesInOrderAsync(owner, projectId));

        // Past the end: appended.
        HttpResponseMessage appended = await owner.PostAsJsonAsync($"/api/projects/{projectId}/stages", new { Name = "Handover", Order = 99 });
        Assert.Equal(HttpStatusCode.OK, appended.StatusCode);
        Guid handoverId = (await appended.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("stageId").GetGuid();
        Assert.Equal(["Survey", "Design", "Handover"], await NamesInOrderAsync(owner, projectId));

        // Update: rename and move Survey to the end.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsJsonAsync($"/api/projects/{projectId}/stages/{surveyId}", new
        {
            Name = "Final survey",
            Order = 3,
            EstimatedStartDate = "2027-01-05",
        })).StatusCode);
        Assert.Equal(["Design", "Handover", "Final survey"], await NamesInOrderAsync(owner, projectId));
        JsonElement survey = await owner.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/stages/{surveyId}");
        Assert.Equal("Final survey", survey.GetProperty("name").GetString());

        // Delete an unused stage: order closes up.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/projects/{projectId}/stages/{handoverId}")).StatusCode);
        Assert.Equal(["Design", "Final survey"], await NamesInOrderAsync(owner, projectId));
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"/api/projects/{projectId}/stages/{handoverId}")).StatusCode);

        // A satisfied gate makes the stage not deletable.
        HttpResponseMessage gate = await owner.PostAsJsonAsync($"/api/projects/{projectId}/stages/{designId}/gates",
            new { ConditionType = 4, Trigger = 2, Label = "Signed off" });
        Assert.Equal(HttpStatusCode.OK, gate.StatusCode);
        Guid gateId = (await gate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("gateConditionId").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent,
            (await owner.PostAsJsonAsync($"/api/projects/{projectId}/gates/{gateId}/satisfy", new { Note = "ok" })).StatusCode);
        HttpResponseMessage refused = await owner.DeleteAsync($"/api/projects/{projectId}/stages/{designId}");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("projects.stages.NotDeletable", await refused.Content.ReadAsStringAsync());
        Assert.Equal(["Design", "Final survey"], await NamesInOrderAsync(owner, projectId));
    }

    private static async Task<string[]> NamesInOrderAsync(HttpClient client, Guid projectId)
    {
        JsonElement stages = await client.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/stages");
        JsonElement[] rows = [.. stages.EnumerateArray().OrderBy(s => s.GetProperty("order").GetInt32())];
        Assert.Equal(Enumerable.Range(1, rows.Length), rows.Select(s => s.GetProperty("order").GetInt32()));
        return [.. rows.Select(s => s.GetProperty("name").GetString()!)];
    }

    private HttpClient Client(string userId, params string[] permissions)
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(userId, permissions));
        return client;
    }
}
