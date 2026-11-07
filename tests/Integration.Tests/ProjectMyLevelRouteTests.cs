using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>L1 (C2): every endpoint that returns a <c>ProjectResponse</c> or a <c>ProjectDetailsResponse</c> — the paged
/// list, the single get and the details — carries <c>myLevel</c> as an int between 1 (Viewer) and 3 (Manager).</summary>
[Collection(ApiCollection.Name)]
public sealed class ProjectMyLevelRouteTests(ApiFixture fixture)
{
    private static readonly string[] OfficeCodes =
    [
        "project.create", "project.read", "clients.create", "clients.read", "project.manage_members",
    ];

    private static readonly int[] AllManager = [3, 3, 3];
    private static readonly int[] AllViewer = [1, 1, 1];

    [Fact]
    public async Task L1_EveryProjectRead_ReturnsMyLevelAsAnIntFrom1To3()
    {
        string suffix = Guid.NewGuid().ToString("N")[..10];
        (_, string ownerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"ml-owner-{suffix}@example.com");
        (Guid viewerId, string viewerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"ml-viewer-{suffix}@example.com");
        HttpClient owner = Client(ownerUserId, OfficeCodes);

        string name = $"MyLevel {suffix}";
        Guid projectId = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, $"ML-{suffix}", name: name);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/projects/{projectId}/members", new { EmployeeId = viewerId, Access = 1 })).StatusCode);
        HttpClient viewer = Client(viewerUserId, "project.read");

        int[] ownerLevels = await ReadMyLevelsAsync(owner, projectId, name);
        int[] viewerLevels = await ReadMyLevelsAsync(viewer, projectId, name);
        Assert.Equal(AllManager, ownerLevels);
        Assert.Equal(AllViewer, viewerLevels);
    }

    /// <summary>[list, get, details]; each must be a JSON number in 1..3.</summary>
    private static async Task<int[]> ReadMyLevelsAsync(HttpClient client, Guid projectId, string name)
    {
        JsonElement list = await GetJsonAsync(client, $"/api/projects?pageNumber=1&pageSize=50&nameFilter={Uri.EscapeDataString(name)}");
        JsonElement row = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == projectId);
        JsonElement get = await GetJsonAsync(client, $"/api/projects/{projectId}");
        JsonElement details = await GetJsonAsync(client, $"/api/projects/{projectId}/details");

        int[] levels = [Level(row), Level(get), Level(details)];
        Assert.All(levels, l => Assert.InRange(l, 1, 3));
        return levels;
    }

    private static int Level(JsonElement response)
    {
        JsonElement myLevel = response.GetProperty("myLevel");
        Assert.Equal(JsonValueKind.Number, myLevel.ValueKind);
        return myLevel.GetInt32();
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string route)
    {
        HttpResponseMessage response = await client.GetAsync(route);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{route}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private HttpClient Client(string userId, params string[] permissions)
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(userId, permissions));
        return client;
    }
}
