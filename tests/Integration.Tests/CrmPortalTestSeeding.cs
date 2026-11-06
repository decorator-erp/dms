using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Integration.Tests;

/// <summary>crm-complete part F: an office user, a client with a portal login and a Stage-workflow project
/// (one stage, from a one-stage template) with an open requirement period.</summary>
internal sealed record PortalWorld(
    HttpClient Office, Guid OfficeEmployeeId, string OfficeUserId, Guid ClientId, string ClientUserId,
    Guid ProjectId, Guid StageId, Guid PeriodId);

internal static class CrmPortalTestSeeding
{
    public static readonly string[] OfficeCodes =
    [
        "project.create", "project.read", "project.read_all", "stages.read", "stages.manage", "clients.create", "clients.read",
        "crm.periods.open", "crm.periods.read", "crm.items.create", "crm.items.read", "crm.items.send", "crm.briefs.read",
        "crm.briefs.manage", "crm.briefs.send", "crm.meetings.schedule", "crm.meetings.read", "crm.spaces.read",
        "crm.spaces.manage", "crm.catalog.read", "crm.catalog.manage", "crm.space_types.read", "crm.space_types.manage",
        "crm.artifacts.read", "crm.artifacts.manage", "documents.upload", "documents.read",
    ];

    public static async Task<PortalWorld> CreateAsync(ApiFixture fixture, string label, HttpClient? office = null, Guid? officeEmployeeId = null, string? officeUserId = null)
    {
        string suffix = Guid.NewGuid().ToString("N")[..10];
        if (office is null)
        {
            (Guid employeeId, string userId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"{label}-office-{suffix}@example.com");
            office = Client(fixture, userId, OfficeCodes);
            officeEmployeeId = employeeId;
            officeUserId = userId;
        }

        HttpResponseMessage clientResponse = await office.PostAsJsonAsync("/api/clients", ClientRequests.NewClientBody($"{label}{suffix}"));
        Assert.Equal(HttpStatusCode.Created, clientResponse.StatusCode);
        Guid clientId = (await clientResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("clientId").GetGuid();
        string clientUserId = (await office.GetFromJsonAsync<JsonElement>($"/api/clients/{clientId}")).GetProperty("appUserId").GetString()!;
        await DrainAsync(fixture);

        HttpResponseMessage template = await office.PostAsJsonAsync("/api/stage-templates", new { Name = $"T-{label}-{suffix}", Type = 2 });
        Guid templateId = (await template.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("stageTemplateId").GetGuid();
        Assert.True((await office.PostAsJsonAsync($"/api/stage-templates/{templateId}/stages",
            new { Name = "Design", Order = 1, WeightPercent = 100m, Description = (string?)null })).IsSuccessStatusCode);
        HttpResponseMessage project = await office.PostAsJsonAsync("/api/projects", new
        {
            Code = $"{label.ToUpperInvariant()}-{suffix}",
            Name = $"Portal {label} {suffix}",
            Address = "1 Portal Rd",
            WorkflowType = 1,
            ClientId = clientId,
            StageTemplateId = templateId,
        });
        Assert.Equal(HttpStatusCode.OK, project.StatusCode);
        Guid projectId = (await project.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("projectId").GetGuid();
        Guid stageId = Assert.Single((await office.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/stages")).EnumerateArray()).GetProperty("id").GetGuid();

        HttpResponseMessage period = await office.PostAsJsonAsync($"/api/crm/projects/{projectId}/periods",
            new { ProjectStageId = stageId, ClosesAtUtc = DateTime.UtcNow.AddDays(3) });
        Assert.True(period.IsSuccessStatusCode, await period.Content.ReadAsStringAsync());
        Guid periodId = (await period.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("periodId").GetGuid();

        return new PortalWorld(office, officeEmployeeId!.Value, officeUserId!, clientId, clientUserId, projectId, stageId, periodId);
    }

    public static async Task<Guid> CreateItemAsync(PortalWorld world, string title, bool send)
    {
        HttpResponseMessage item = await world.Office.PostAsJsonAsync($"/api/crm/periods/{world.PeriodId}/items",
            new { world.ProjectId, Category = 1, Title = title, Priority = 2, Room = "Living", Description = $"{title} details" });
        Assert.True(item.IsSuccessStatusCode, await item.Content.ReadAsStringAsync());
        Guid itemId = (await item.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("itemId").GetGuid();
        if (send)
        {
            HttpResponseMessage sent = await world.Office.PutAsync($"/api/crm/requirement-items/{itemId}/send", null);
            Assert.True(sent.StatusCode == HttpStatusCode.NoContent, await sent.Content.ReadAsStringAsync());
        }

        return itemId;
    }

    public static HttpClient Client(ApiFixture fixture, string userId, params string[] permissions)
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(userId, permissions));
        return client;
    }

    public static Task DrainAsync(ApiFixture fixture) =>
        fixture.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single().ProcessPendingAsync(CancellationToken.None);
}
