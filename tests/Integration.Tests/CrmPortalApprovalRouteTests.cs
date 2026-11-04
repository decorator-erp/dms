using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// B1.2 / B1.3 over HTTP, through the real composition root: the client lists and decides its own approval
/// with only the portal's <c>_own</c> claims, and the auth order holds — 403 (route claim) before 404/409.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CrmPortalApprovalRouteTests(ApiFixture fixture)
{
    [Fact]
    public async Task Client_ListsAndDecidesItsBriefApproval_OverHttp_403_400_204_409()
    {
        string suffix = Guid.NewGuid().ToString("N");
        (_, string employeeUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"portal-approvals-{suffix}@example.com");

        HttpClient office = fixture.CreateClient();
        office.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(
            employeeUserId, "project.create", "project.read_all", "stages.read", "stages.manage",
            "clients.create", "clients.read", "crm.briefs.manage", "crm.briefs.send", "crm.briefs.read"));

        HttpResponseMessage clientResponse = await office.PostAsJsonAsync("/api/clients", ClientRequests.NewClientBody("portal-approvals"));
        Assert.Equal(HttpStatusCode.Created, clientResponse.StatusCode);
        Guid clientId = (await clientResponse.Content.ReadFromJsonAsync<ClientIdBody>())!.ClientId;
        string clientUserId = (await office.GetFromJsonAsync<ClientAppUserBody>($"/api/clients/{clientId}"))!.AppUserId;
        await DrainAsync();

        Guid templateId = (await (await office.PostAsJsonAsync("/api/stage-templates", new { Name = $"Tmpl-{suffix}", Type = 2 }))
            .Content.ReadFromJsonAsync<IdBody>())!.StageTemplateId;
        Assert.True((await office.PostAsJsonAsync(
            $"/api/stage-templates/{templateId}/stages",
            new { Name = "Design", Order = 1, WeightPercent = 100m, Description = (string?)null })).IsSuccessStatusCode);
        HttpResponseMessage projectResponse = await office.PostAsJsonAsync("/api/projects", new
        {
            Code = $"P-{suffix}",
            Name = "Portal Approvals",
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
            Data = new { UnitType = 1, UnitCondition = 1, Address = "Portal Addr" },
        });
        Assert.Equal(HttpStatusCode.Created, briefResponse.StatusCode);
        Guid briefId = (await briefResponse.Content.ReadFromJsonAsync<BriefIdBody>())!.BriefId;
        Assert.Equal(HttpStatusCode.NoContent, (await office.PutAsync($"/api/crm/briefs/{briefId}/send", content: null)).StatusCode);

        HttpClient portal = fixture.CreateClient();
        portal.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(
            clientUserId, "crm.portal", "crm.approvals.view_own", "crm.approvals.decide_own"));
        HttpClient noClaims = fixture.CreateClient();
        noClaims.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", fixture.IssueTestToken(clientUserId, "crm.portal"));

        // The list: the round just sent, money-free and office-free.
        HttpResponseMessage list = await portal.GetAsync("/api/crm/portal/approvals?status=pending");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        JsonElement row = Assert.Single(listJson.RootElement.GetProperty("items").EnumerateArray());
        Guid approvalId = row.GetProperty("id").GetGuid();
        Assert.Equal(2, row.GetProperty("subjectType").GetInt32());
        Assert.Equal(briefId, row.GetProperty("subjectId").GetGuid());
        Assert.Equal("Portal Approvals", row.GetProperty("projectName").GetString());
        Assert.Equal(1, row.GetProperty("round").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await portal.GetAsync("/api/crm/portal/approvals?pageSize=201")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await portal.GetAsync("/api/crm/portal/approvals?status=open")).StatusCode);

        // 403 on the route claim, before any lookup — even for an approval that does not exist.
        Assert.Equal(HttpStatusCode.Forbidden, (await noClaims.GetAsync("/api/crm/portal/approvals")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await noClaims.PutAsJsonAsync(
            $"/api/crm/portal/approvals/{Guid.NewGuid()}/decide", new { Decision = 1 })).StatusCode);

        string decideUrl = $"/api/crm/portal/approvals/{approvalId}/decide";
        Assert.Equal(HttpStatusCode.NotFound, (await portal.PutAsJsonAsync(
            $"/api/crm/portal/approvals/{Guid.NewGuid()}/decide", new { Decision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await portal.PutAsJsonAsync(decideUrl, new { Decision = 3 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await portal.PutAsJsonAsync(decideUrl, new { Decision = 2, Comment = "No" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await portal.PutAsJsonAsync(decideUrl, new { Decision = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await portal.PutAsJsonAsync(
            decideUrl, new { Decision = 3, Comment = "Too late" })).StatusCode);

        await DrainAsync();
        await DrainAsync();
        using var decided = JsonDocument.Parse(await (await portal.GetAsync("/api/crm/portal/approvals?status=decided")).Content.ReadAsStringAsync());
        Assert.Equal(1, Assert.Single(decided.RootElement.GetProperty("items").EnumerateArray()).GetProperty("decision").GetInt32());
        using var brief = JsonDocument.Parse(await (await office.GetAsync($"/api/crm/stages/{stageId}/brief")).Content.ReadAsStringAsync());
        Assert.Equal(3, brief.RootElement.GetProperty("status").GetInt32());
    }

    private Task DrainAsync() =>
        fixture.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single().ProcessPendingAsync(CancellationToken.None);

    private sealed record IdBody(Guid StageTemplateId);
    private sealed record ProjectIdBody(Guid ProjectId);
    private sealed record ProjectStageBody(Guid Id);
    private sealed record ClientIdBody(Guid ClientId);
    private sealed record ClientAppUserBody(string AppUserId);
    private sealed record BriefIdBody(Guid BriefId);
}
