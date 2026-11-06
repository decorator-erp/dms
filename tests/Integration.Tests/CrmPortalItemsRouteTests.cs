using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>F1 (D5): the portal requirement-items list. Only the calling client's active projects; office
/// drafts stay hidden; filters, paging; the latest decision and its comment; no office-only fields.</summary>
[Collection(ApiCollection.Name)]
public sealed class CrmPortalItemsRouteTests(ApiFixture fixture)
{
    [Fact]
    public async Task Client_sees_only_its_sent_items_with_the_decision_comment_filters_and_paging()
    {
        PortalWorld a = await CrmPortalTestSeeding.CreateAsync(fixture, "f1a");
        PortalWorld b = await CrmPortalTestSeeding.CreateAsync(fixture, "f1b", a.Office, a.OfficeEmployeeId, a.OfficeUserId);
        Guid sent = await CrmPortalTestSeeding.CreateItemAsync(a, "Bigger window", send: true);
        Guid second = await CrmPortalTestSeeding.CreateItemAsync(a, "Oak floor", send: true);
        Guid draft = await CrmPortalTestSeeding.CreateItemAsync(a, "Office draft", send: false);
        Guid otherClients = await CrmPortalTestSeeding.CreateItemAsync(b, "Not yours", send: true);

        HttpClient portal = CrmPortalTestSeeding.Client(fixture, a.ClientUserId,
            "crm.portal", "requirements.view_own", "crm.approvals.view_own", "crm.approvals.decide_own");
        const string route = "/api/crm/portal/requirement-items";

        Assert.Equal(HttpStatusCode.Forbidden, (await CrmPortalTestSeeding.Client(fixture, a.ClientUserId, "crm.portal").GetAsync(route)).StatusCode);

        JsonElement page = await portal.GetFromJsonAsync<JsonElement>($"{route}?pageNumber=1&pageSize=50");
        Guid[] ids = [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())];
        Assert.Contains(sent, ids);
        Assert.Contains(second, ids);
        Assert.DoesNotContain(draft, ids);
        Assert.DoesNotContain(otherClients, ids);

        // No office-only fields.
        string raw = await (await portal.GetAsync($"{route}?pageNumber=1&pageSize=50")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("createdByEmployeeId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("projectTaskId", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("noteId", raw, StringComparison.OrdinalIgnoreCase);

        // Decide one with a comment: the list carries the latest decision and its comment.
        JsonElement approvals = await portal.GetFromJsonAsync<JsonElement>("/api/crm/portal/approvals?status=pending&pageNumber=1&pageSize=50");
        Guid approvalId = approvals.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("subjectId").GetGuid() == sent).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent,
            (await portal.PutAsJsonAsync($"/api/crm/portal/approvals/{approvalId}/decide", new { Decision = 3, Comment = "Make it wider" })).StatusCode);
        await CrmPortalTestSeeding.DrainAsync(fixture);

        JsonElement decided = (await portal.GetFromJsonAsync<JsonElement>($"{route}?pageNumber=1&pageSize=50"))
            .GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == sent);
        Assert.Equal("Make it wider", decided.GetProperty("decisionComment").GetString());
        Assert.Equal(3, decided.GetProperty("decision").GetInt32());
        Assert.Equal("Bigger window", decided.GetProperty("title").GetString());

        // Filters: status (PendingApproval = 2), period, project; another client's project is 404.
        Guid[] pending = [.. (await portal.GetFromJsonAsync<JsonElement>($"{route}?status=2&pageNumber=1&pageSize=50"))
            .GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid())];
        Assert.Contains(second, pending);
        Assert.DoesNotContain(sent, pending);
        Assert.Equal(2, (await portal.GetFromJsonAsync<JsonElement>($"{route}?periodId={a.PeriodId}&pageNumber=1&pageSize=50")).GetProperty("totalCount").GetInt32());
        Assert.Equal(2, (await portal.GetFromJsonAsync<JsonElement>($"{route}?projectId={a.ProjectId}&pageNumber=1&pageSize=50")).GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync($"{route}?projectId={b.ProjectId}&pageNumber=1&pageSize=50")).StatusCode);

        // Paging.
        JsonElement one = await portal.GetFromJsonAsync<JsonElement>($"{route}?pageNumber=1&pageSize=1");
        Assert.Single(one.GetProperty("items").EnumerateArray());
        Assert.Equal(2, one.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await portal.GetAsync($"{route}?pageNumber=1&pageSize=201")).StatusCode);

        // An office user is not a portal client.
        Assert.Equal(HttpStatusCode.Forbidden, (await CrmPortalTestSeeding.Client(fixture, a.OfficeUserId, "requirements.view_own")
            .GetAsync($"{route}?pageNumber=1&pageSize=10")).StatusCode);
    }
}
