using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// BF-1 end to end: an employee holding exactly the employee baseline (v1 + v5 + v6, as
/// <c>BaselineClaimSeeder</c> grants it) and Editor on a project can do what a surveyor's lookups need there —
/// read the project's spaces, read the catalog, list and upload the project's documents — and none of it on a
/// project they are not on. The token carries the codes literally, the same codes the seeder writes, so a code
/// dropped from v6 fails here as well as in <c>BaselineClaimSeedingTests</c>.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class EmployeeSurveyBaselineRouteTests(ApiFixture fixture)
{
    private static readonly string[] EmployeeBaseline =
    [
        // v1
        "employees.view_own", "notifications.view_own",
        // v5
        "project.read", "stages.read", "scope.read", "stage_tasks.read", "stage_tasks.complete",
        "sites.read", "sites.create", "sites.update", "sites.submit",
        // v6 (BF-1)
        "crm.spaces.read", "crm.catalog.read", "documents.read", "documents.upload",
    ];

    private static readonly string[] OwnerCodes =
    [
        "project.create", "project.read", "project.manage_members", "clients.create", "clients.read",
        "stages.read", "stages.manage", "scope.read", "scope.manage", "scope.confirm",
    ];

    [Fact]
    public async Task An_editor_employee_on_the_baseline_reads_spaces_catalog_and_documents_and_uploads_only_on_their_project()
    {
        string suffix = Guid.NewGuid().ToString("N")[..10];
        (_, string ownerUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"bf1-owner-{suffix}@example.com");
        (Guid surveyorId, string surveyorUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"bf1-surveyor-{suffix}@example.com");
        HttpClient owner = Client(ownerUserId, OwnerCodes);

        Guid onProject = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, $"BF1A-{suffix}");
        Guid otherProject = await ProjectTestSeeding.CreateFreeStyleProjectAsync(owner, $"BF1B-{suffix}");
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync(
            $"/api/projects/{onProject}/members", new { EmployeeId = surveyorId, Access = 2 })).StatusCode);

        HttpClient surveyor = Client(surveyorUserId, EmployeeBaseline);

        // Their project: every lookup answers.
        await AssertOkAsync(surveyor.GetAsync($"/api/crm/projects/{onProject}/spaces"));
        await AssertOkAsync(surveyor.GetAsync("/api/crm/catalog/tree"));
        await AssertOkAsync(surveyor.GetAsync($"/api/projects/{onProject}/documents"));
        await AssertOkAsync(surveyor.PostAsync($"/api/projects/{onProject}/documents", File("site-photo.txt")));

        // A project they are not on: the claim is there, the project is not — 404, never its content.
        Assert.Equal(HttpStatusCode.NotFound, (await surveyor.GetAsync($"/api/crm/projects/{otherProject}/spaces")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await surveyor.GetAsync($"/api/projects/{otherProject}/documents")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await surveyor.PostAsync($"/api/projects/{otherProject}/documents", File("x.txt"))).StatusCode);

        // The baseline stops at upload: no delete, no catalog or space management.
        Assert.Equal(HttpStatusCode.Forbidden, (await surveyor.DeleteAsync($"/api/project-documents/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await surveyor.PutAsJsonAsync(
            $"/api/crm/projects/{onProject}/spaces/order", new { SpaceIds = Array.Empty<Guid>() })).StatusCode);
    }

    private static async Task AssertOkAsync(Task<HttpResponseMessage> call)
    {
        HttpResponseMessage response = await call;
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    private static MultipartFormDataContent File(string name)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes($"contents of {name}"));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        return new MultipartFormDataContent { { content, "file", name } };
    }

    private HttpClient Client(string userId, params string[] permissions)
    {
        HttpClient client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(userId, permissions));
        return client;
    }
}
