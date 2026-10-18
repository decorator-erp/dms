using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// The CRM brief fixes' HTTP surface, through the real composition root: the create's Location (the
/// stage route, L2), the stage route, the
/// two-audience round-diff route (route claims pick the audience; neither claim is a 403), and the
/// client's money-free portal view.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CrmBriefRouteTests(ApiFixture fixture)
{
    [Fact]
    public async Task StageRoute_RoundDiffAudiences_AndPortalView_OverHttp()
    {
        string suffix = Guid.NewGuid().ToString("N");
        (_, string employeeUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"brief-routes-{suffix}@example.com");

        HttpClient office = fixture.CreateClient();
        office.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(
            employeeUserId, "project.create", "project.read_all", "stages.read", "stages.manage",
            "clients.create", "clients.read", "crm.briefs.manage", "crm.briefs.send", "crm.briefs.read"));

        HttpResponseMessage clientResponse = await office.PostAsJsonAsync("/api/clients", ClientRequests.NewClientBody("routes"));
        Assert.Equal(HttpStatusCode.Created, clientResponse.StatusCode);
        Guid clientId = (await clientResponse.Content.ReadFromJsonAsync<ClientIdBody>())!.ClientId;
        string clientUserId = (await office.GetFromJsonAsync<ClientAppUserBody>($"/api/clients/{clientId}"))!.AppUserId;

        Guid templateId = (await (await office.PostAsJsonAsync("/api/stage-templates", new { Name = $"Tmpl-{suffix}", Type = 2 }))
            .Content.ReadFromJsonAsync<IdBody>())!.StageTemplateId;
        Assert.True((await office.PostAsJsonAsync(
            $"/api/stage-templates/{templateId}/stages",
            new { Name = "Design", Order = 1, WeightPercent = 100m, Description = (string?)null })).IsSuccessStatusCode);
        HttpResponseMessage projectResponse = await office.PostAsJsonAsync("/api/projects", new
        {
            Code = $"P-{suffix}",
            Name = "Brief Routes",
            Address = "1 Test Rd",
            WorkflowType = 1,
            ClientId = clientId,
            StageTemplateId = templateId,
        });
        Assert.Equal(HttpStatusCode.OK, projectResponse.StatusCode);
        Guid projectId = (await projectResponse.Content.ReadFromJsonAsync<ProjectIdBody>())!.ProjectId;
        Guid stageId = Assert.Single((await office.GetFromJsonAsync<List<ProjectStageBody>>($"/api/projects/{projectId}/stages"))!).Id;

        HttpResponseMessage briefResponse = await office.PostAsJsonAsync($"/api/crm/projects/{projectId}/brief", new
        {
            ProjectStageId = stageId,
            Data = new { UnitType = 1, UnitCondition = 1, Address = "Route Addr", ExpectedBudget = 12345m },
        });
        Assert.Equal(HttpStatusCode.Created, briefResponse.StatusCode);
        Assert.Equal($"/api/crm/stages/{stageId}/brief", briefResponse.Headers.Location?.OriginalString);
        Guid briefId = (await briefResponse.Content.ReadFromJsonAsync<BriefIdBody>())!.BriefId;
        Assert.Equal(HttpStatusCode.NoContent, (await office.PutAsync($"/api/crm/briefs/{briefId}/send", content: null)).StatusCode);

        // The stage route finds the stage's brief.
        BriefIdOnly? byStage = await office.GetFromJsonAsync<BriefIdOnly>($"/api/crm/stages/{stageId}/brief");
        Assert.Equal(briefId, byStage!.Id);

        // Office audience (crm.briefs.read): the budget is part of round 1.
        HttpResponseMessage officeDiff = await office.GetAsync($"/api/crm/briefs/{briefId}/rounds/1/diff");
        Assert.Equal(HttpStatusCode.OK, officeDiff.StatusCode);
        Assert.Contains((await officeDiff.Content.ReadFromJsonAsync<DiffBody>())!.Changes, c => c.Field == "ExpectedBudget");

        // Client audience (crm.briefs.view_own only): same round, no budget anywhere in the body.
        HttpClient portal = fixture.CreateClient();
        portal.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.IssueTestToken(clientUserId, "crm.briefs.view_own"));
        HttpResponseMessage clientDiff = await portal.GetAsync($"/api/crm/briefs/{briefId}/rounds/1/diff");
        Assert.Equal(HttpStatusCode.OK, clientDiff.StatusCode);
        string clientDiffJson = await clientDiff.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ExpectedBudget", clientDiffJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Route Addr", clientDiffJson);

        // Neither claim: 403 before any lookup.
        HttpClient nobody = fixture.CreateClient();
        nobody.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.IssueTestToken(clientUserId, "crm.portal"));
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync($"/api/crm/briefs/{briefId}/rounds/1/diff")).StatusCode);

        // The portal view: the sent round, money-free.
        HttpResponseMessage view = await portal.GetAsync($"/api/crm/portal/projects/{projectId}/brief?stageId={stageId}");
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        using var viewJson = JsonDocument.Parse(await view.Content.ReadAsStringAsync());
        JsonElement head = viewJson.RootElement.GetProperty("head");
        Assert.Equal(1, head.GetProperty("round").GetInt32());
        Assert.False(head.TryGetProperty("expectedBudget", out _));
        Assert.Equal(0, viewJson.RootElement.GetProperty("spaces").GetArrayLength());
    }

    private sealed record IdBody(Guid StageTemplateId);
    private sealed record ProjectIdBody(Guid ProjectId);
    private sealed record ProjectStageBody(Guid Id);
    private sealed record ClientIdBody(Guid ClientId);
    private sealed record ClientAppUserBody(string AppUserId);
    private sealed record BriefIdBody(Guid BriefId);
    private sealed record BriefIdOnly(Guid Id);
    private sealed record DiffBody(Guid BriefId, int Round, int? PreviousRound, List<ChangeBody> Changes);
    private sealed record ChangeBody(string Field, string? PreviousValue, string? CurrentValue);
}
