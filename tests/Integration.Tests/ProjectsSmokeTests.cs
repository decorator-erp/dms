using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Identity.Contracts;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// G5-09's own verify block: the app boots with all four modules; <c>GET /api/permissions</c> contains
/// the <c>project.*</c> strings; unauthenticated → 401 and authenticated-without-permission → 403 on a
/// mutating Projects route.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProjectsSmokeTests(ApiFixture fixture)
{
    [Fact]
    public async Task GetPermissions_ContainsProjectsModulePermissionStrings()
    {
        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString(), IdentityPermissions.PermissionsManage);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.GetAsync("/api/permissions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        List<ModulePermissionSet>? modules = await response.Content.ReadFromJsonAsync<List<ModulePermissionSet>>();
        Assert.NotNull(modules);

        ModulePermissionSet projectsModule = Assert.Single(modules!, m => m.Module == "Projects");
        Assert.Contains("project.read", projectsModule.Permissions);
        Assert.Contains("project.create", projectsModule.Permissions);
        Assert.Contains("stages.manage_gates", projectsModule.Permissions);
        Assert.Contains("stage_tasks.manage_gates", projectsModule.Permissions);
    }

    [Fact]
    public async Task CreateProject_Unauthenticated_Returns401()
    {
        HttpClient client = fixture.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/projects", new
        {
            Code = "P-1",
            Name = "Smoke",
            Address = "1 Test Rd",
            WorkflowType = 1,
            ClientId = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateProject_AuthenticatedWithoutPermission_Returns403()
    {
        HttpClient client = fixture.CreateClient();
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/projects", new
        {
            Code = "P-1",
            Name = "Smoke",
            Address = "1 Test Rd",
            WorkflowType = 1,
            ClientId = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>SOW-4: the scope of work is office only. A caller holding exactly the seeded client role's
    /// permissions is refused at the route (403) on every scope-of-work route, before any handler runs.</summary>
    [Fact]
    public async Task ScopeOfWork_routes_are_403_for_a_client_user()
    {
        HttpClient client = fixture.CreateClient();
        await client.GetAsync("/health");
        Dictionary<string, HashSet<string>> claims = await SeedingTests.RoleClaimsAsync(fixture.ConnectionString);
        string token = fixture.IssueTestToken(Guid.NewGuid().ToString(), [.. claims.GetValueOrDefault("client") ?? []]);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        string route = $"/api/projects/{Guid.NewGuid()}/scope-of-work";

        HttpResponseMessage[] responses =
        [
            await client.GetAsync(route),
            await client.PutAsJsonAsync(route, new { durationDays = 10, adjustmentAmount = 0 }),
            await client.PostAsJsonAsync($"{route}/items", new { sourceStageIds = new[] { Guid.NewGuid() } }),
            await client.PostAsync($"{route}/items/from-template/{Guid.NewGuid()}", null),
            await client.PutAsJsonAsync($"{route}/items/order", new { itemIds = new[] { Guid.NewGuid() } }),
            await client.PutAsJsonAsync($"{route}/items/{Guid.NewGuid()}", new { name = "x", fee = 1 }),
            await client.DeleteAsync($"{route}/items/{Guid.NewGuid()}"),
        ];

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode));
    }
}
