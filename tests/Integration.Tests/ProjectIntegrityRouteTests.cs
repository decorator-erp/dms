using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>Sites/Projects review, phase 3 (Projects data integrity): the route-level red tests.</summary>
[Collection(ApiCollection.Name)]
public sealed class ProjectIntegrityRouteTests(ApiFixture fixture)
{
    private static readonly string[] OfficeCodes =
    [
        "project.create", "project.read", "project.update", "project.delete", "clients.create", "clients.read",
        "stages.read", "stages.manage", "stages.complete", "stages.manage_gates", "scope.read", "scope.manage",
        "scope.confirm", "stage_tasks.read", "stage_tasks.manage", "stage_tasks.complete",
    ];

    /// <summary>P12: deleting the stage a confirmed scope-of-work item created clears the item's
    /// <c>CreatedStageId</c> in the same save, so the scope never points at a stage that is gone.</summary>
    [Fact]
    public async Task P12_DeletingAStageClearsTheScopeItemThatCreatedIt()
    {
        (HttpClient owner, Guid projectId, Guid stageId) = await ConfirmedProjectAsync("p12");
        JsonElement item = Assert.Single((await owner.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/scope-of-work"))
            .GetProperty("items").EnumerateArray());
        Assert.Equal(stageId, item.GetProperty("createdStageId").GetGuid());

        HttpResponseMessage deleted = await owner.DeleteAsync($"/api/projects/{projectId}/stages/{stageId}");
        Assert.True(deleted.StatusCode == HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync());

        item = Assert.Single((await owner.GetFromJsonAsync<JsonElement>($"/api/projects/{projectId}/scope-of-work"))
            .GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("createdStageId").ValueKind);
    }

    private async Task<(HttpClient Owner, Guid ProjectId, Guid StageId)> ConfirmedProjectAsync(string label)
    {
        string suffix = Guid.NewGuid().ToString("N")[..10];
        (_, string ownerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"{label}-owner-{suffix}@example.com");
        HttpClient owner = Client(ownerUserId, OfficeCodes);
        (Guid projectId, Guid stageId, _) = await ProjectTestSeeding.CreateConfirmedStageProjectAsync(owner, $"{label.ToUpperInvariant()}-{suffix}");
        return (owner, projectId, stageId);
    }

    private HttpClient Client(string userId, params string[] permissions)
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(userId, permissions));
        return client;
    }
}
