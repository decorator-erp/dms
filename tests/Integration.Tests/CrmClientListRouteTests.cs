using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Crm.Contracts;
using Xunit;

namespace Integration.Tests;

/// <summary>D-16 / C-F5: <c>GET /api/clients</c> takes <c>isActive=all|true|false</c> (default true), so
/// the list can show both. Anything else is a 400.</summary>
[Collection(ApiCollection.Name)]
public sealed class CrmClientListRouteTests(ApiFixture fixture)
{
    [Fact]
    public async Task TheIsActiveFilter_AcceptsAllTrueAndFalse_AndAllIsTheirSum()
    {
        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString(), CrmPermissions.Clients.Read, CrmPermissions.Clients.ReadAll);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        int all = await TotalAsync(client, "?isActive=all");
        int active = await TotalAsync(client, "?isActive=true");
        int inactive = await TotalAsync(client, "?isActive=false");

        Assert.Equal(active + inactive, all);
        Assert.Equal(active, await TotalAsync(client, ""));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/clients?isActive=maybe")).StatusCode);
    }

    private static async Task<int> TotalAsync(HttpClient client, string query)
    {
        HttpResponseMessage response = await client.GetAsync($"/api/clients{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Page? page = await response.Content.ReadFromJsonAsync<Page>();
        return page!.TotalCount;
    }

    private sealed record Page(int TotalCount);
}
