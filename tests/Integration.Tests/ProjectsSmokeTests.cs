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
}
