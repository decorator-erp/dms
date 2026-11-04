using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// B2-be over HTTP: <c>GET /api/projects/{projectId}/details</c>. The owner (no read-all claim) gets the page's
/// one-shot shape; an outsider gets 404 from the scope decorator, an unauthorized caller 403; an inactive
/// project is returned as <c>GET /api/projects/{projectId}</c> returns it. And no N+1: a three-stage project's
/// details issue exactly as many SQL statements as a one-stage project's.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProjectDetailsRouteTests(ApiFixture fixture)
{
    [Fact]
    public async Task Owner_gets_the_details_shape_outsider_404_inactive_follows_get_and_no_query_per_stage()
    {
        string suffix = Guid.NewGuid().ToString("N");
        (_, string ownerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"details-owner-{suffix}@example.com");
        (_, string outsiderUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"details-outsider-{suffix}@example.com");

        HttpClient owner = Client(ownerUserId,
            "project.create", "project.read", "project.set_active", "stages.manage", "clients.create", "clients.read");
        HttpResponseMessage clientResponse = await owner.PostAsJsonAsync("/api/clients", ClientRequests.NewClientBody("details"));
        Assert.Equal(HttpStatusCode.Created, clientResponse.StatusCode);
        Guid clientId = (await clientResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("clientId").GetGuid();

        Guid oneStage = await CreateProjectAsync(owner, clientId, $"D1-{suffix}", stages: 1);
        Guid threeStages = await CreateProjectAsync(owner, clientId, $"D3-{suffix}", stages: 3);

        // Shape.
        HttpResponseMessage response = await owner.GetAsync($"/api/projects/{threeStages}/details");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            JsonElement root = json.RootElement;
            JsonElement project = root.GetProperty("project");
            Assert.Equal(threeStages, project.GetProperty("id").GetGuid());
            Assert.Equal("Portal Client", project.GetProperty("clientName").GetString());
            Assert.Equal(clientId, project.GetProperty("clientId").GetGuid());
            Assert.Equal(3, project.GetProperty("stageCount").GetInt32());
            Assert.True(project.GetProperty("isActive").GetBoolean());
            JsonElement[] stages = [.. root.GetProperty("stages").EnumerateArray()];
            Assert.Equal(3, stages.Length);
            Assert.Equal(["Stage 1", "Stage 2", "Stage 3"], stages.Select(s => s.GetProperty("name").GetString()));
            foreach (JsonElement stage in stages)
            {
                Assert.Equal(JsonValueKind.Array, stage.GetProperty("tasks").ValueKind);
                Assert.Equal(JsonValueKind.Array, stage.GetProperty("taskCounts").ValueKind);
                Assert.Equal(0, stage.GetProperty("gates").GetProperty("total").GetInt32());
            }

            Assert.Equal(JsonValueKind.Array, root.GetProperty("unstagedTasks").ValueKind);
            Assert.Equal(JsonValueKind.Array, root.GetProperty("taskCounts").ValueKind);
            Assert.Equal(0, root.GetProperty("gates").GetProperty("pending").GetInt32());
        }

        // No N+1: the statement count does not grow with the stage count.
        int oneStageStatements = await CountStatementsAsync(owner, oneStage);
        int threeStageStatements = await CountStatementsAsync(owner, threeStages);
        Assert.Equal(oneStageStatements, threeStageStatements);
        Assert.True(oneStageStatements >= 5, $"project, stages, tasks, gates and the client read; got {oneStageStatements}");

        // An outsider, and an unknown id: 404. No route claim: 403.
        HttpClient outsider = Client(outsiderUserId, "project.read");
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/projects/{threeStages}/details")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/projects/{Guid.NewGuid()}/details")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(ownerUserId).GetAsync($"/api/projects/{threeStages}/details")).StatusCode);

        // Inactive: the details read follows GET /api/projects/{id} exactly.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PutAsync($"/api/projects/{threeStages}/active?isActive=false", null)).StatusCode);
        HttpResponseMessage single = await owner.GetAsync($"/api/projects/{threeStages}");
        HttpResponseMessage details = await owner.GetAsync($"/api/projects/{threeStages}/details");
        Assert.Equal(single.StatusCode, details.StatusCode);
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        Assert.False((await single.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isActive").GetBoolean());
        Assert.False((await details.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("project").GetProperty("isActive").GetBoolean());
    }

    private async Task<int> CountStatementsAsync(HttpClient client, Guid projectId)
    {
        await client.GetAsync($"/api/projects/{projectId}/details"); // warm any first-call caches
        fixture.ClearCapturedSql();
        HttpResponseMessage response = await client.GetAsync($"/api/projects/{projectId}/details");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return fixture.CapturedSql.Count;
    }

    private static async Task<Guid> CreateProjectAsync(HttpClient owner, Guid clientId, string code, int stages)
    {
        HttpResponseMessage template = await owner.PostAsJsonAsync("/api/stage-templates", new { Name = $"Tmpl-{code}", Type = 2 });
        Assert.True(template.IsSuccessStatusCode, await template.Content.ReadAsStringAsync());
        Guid templateId = (await template.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("stageTemplateId").GetGuid();
        for (int i = 1; i <= stages; i++)
        {
            HttpResponseMessage stage = await owner.PostAsJsonAsync(
                $"/api/stage-templates/{templateId}/stages",
                new { Name = $"Stage {i}", Order = i, WeightPercent = 10m, Description = (string?)null });
            Assert.True(stage.IsSuccessStatusCode, await stage.Content.ReadAsStringAsync());
        }

        HttpResponseMessage project = await owner.PostAsJsonAsync("/api/projects", new
        {
            Code = code,
            Name = $"Details {code}",
            Address = "1 Details Rd",
            WorkflowType = 1,
            ClientId = clientId,
            StageTemplateId = templateId,
        });
        Assert.Equal(HttpStatusCode.OK, project.StatusCode);
        return (await project.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("projectId").GetGuid();
    }

    private HttpClient Client(string userId, params string[] permissions)
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(userId, permissions));
        return client;
    }
}
