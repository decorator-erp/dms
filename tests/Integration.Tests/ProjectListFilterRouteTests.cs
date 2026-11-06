using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>E8: <c>GET /api/projects</c> gains <c>clientId</c>, <c>search</c> (name or code, case-insensitive,
/// trimmed) and <c>mine</c>; scope still applies; paging is capped (pageSize ≤ 200, pageNumber ≤ 1e6).</summary>
[Collection(ApiCollection.Name)]
public sealed class ProjectListFilterRouteTests(ApiFixture fixture)
{
    [Fact]
    public async Task ClientId_search_mine_scope_and_paging_caps()
    {
        string suffix = Guid.NewGuid().ToString("N")[..10];
        (_, string ownerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"pl-owner-{suffix}@example.com");
        (_, string outsiderUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"pl-out-{suffix}@example.com");
        HttpClient owner = Client(ownerUserId, "project.create", "project.read", "clients.create", "clients.read");

        Guid clientA = await ProjectTestSeeding.CreateClientAsync(owner, $"pla{suffix}");
        Guid clientB = await ProjectTestSeeding.CreateClientAsync(owner, $"plb{suffix}");
        Guid alpha = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, $"ALP-{suffix}", clientA, $"Alpha Tower {suffix}");
        Guid beta = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, $"BET-{suffix}", clientA, $"Beta Villa {suffix}");
        Guid gamma = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, $"GAM-{suffix}", clientB, $"Gamma Flat {suffix}");

        Assert.Equal([alpha, beta], await IdsAsync(owner, $"clientId={clientA}"));
        Assert.Equal([gamma], await IdsAsync(owner, $"clientId={clientB}"));
        Assert.Equal([alpha], await IdsAsync(owner, $"search={Uri.EscapeDataString($"  alpha tower {suffix}  ")}"));
        Assert.Equal([beta], await IdsAsync(owner, $"search={Uri.EscapeDataString($"bet-{suffix}".ToUpperInvariant())}&clientId={clientA}"));
        Assert.Equal([gamma], await IdsAsync(owner, $"search=gam-{suffix}&mine=true"));
        Assert.Empty(await IdsAsync(owner, $"search=gam-{suffix}&clientId={clientA}"));

        // Scope: an outsider's search finds nothing.
        Assert.Empty(await IdsAsync(Client(outsiderUserId, "project.read"), $"search={suffix}"));

        // Paging caps.
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/projects?pageNumber=1&pageSize=201")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/projects?pageNumber=1000001&pageSize=10")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync("/api/projects?pageNumber=0&pageSize=10")).StatusCode);
    }

    private static async Task<Guid[]> IdsAsync(HttpClient client, string query)
    {
        HttpResponseMessage response = await client.GetAsync($"/api/projects?pageNumber=1&pageSize=200&{query}");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        JsonElement page = await response.Content.ReadFromJsonAsync<JsonElement>();
        return [.. page.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetGuid())];
    }

    private HttpClient Client(string userId, params string[] permissions)
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(userId, permissions));
        return client;
    }
}
