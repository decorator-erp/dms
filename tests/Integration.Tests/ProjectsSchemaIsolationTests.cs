using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// G5-10's own verify block: EF logging captured, no SQL issued while resolving a Projects-owned route
/// touches the <c>identity.</c>/<c>hr.</c>/<c>approvals.</c> schemas. The caller carries
/// <c>project.read_all</c>, so <c>ProjectAccessGuard</c>'s <c>ReadAll</c> short-circuit means the query
/// under test never calls <c>IHrDirectory</c> either — the whole operation is provably
/// <c>ProjectsDbContext</c>-only. Contract calls and FKs are the only allowed crossings (they happen
/// inside Postgres, not in queries) — this test proves the query path itself never joins across schemas.
/// Runs entirely through the REAL host (<c>ApiFixture</c>'s <c>WebApplicationFactory&lt;Program&gt;</c>),
/// same shape as <c>CrossModuleSmokeTests</c> (G3-01) and <c>ProjectsSmokeTests</c> (G5-09).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProjectsSchemaIsolationTests(ApiFixture fixture)
{
    [Fact]
    public async Task GetProjectTasks_WithReadAllBypass_IssuesNoSqlTouchingIdentityHrOrApprovalsSchemas()
    {
        HttpClient client = fixture.CreateClient();
        string suffix = Guid.NewGuid().ToString("N");

        (_, string appUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(
            fixture, $"schema-{suffix}@example.com");

        string setupToken = fixture.IssueTestToken(
            appUserId, "employees.create", "stages.manage", "project.create", "project.read_all", "stage_tasks.read");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", setupToken);

        HttpResponseMessage templateResponse = await client.PostAsJsonAsync("/api/stage-templates", new { Name = $"Tmpl-{suffix}", Type = 2 });
        Assert.Equal(HttpStatusCode.OK, templateResponse.StatusCode);
        Guid templateId = (await templateResponse.Content.ReadFromJsonAsync<IdBody>())!.StageTemplateId;

        HttpResponseMessage stageResponse = await client.PostAsJsonAsync(
            $"/api/stage-templates/{templateId}/stages", new { Name = "Only Stage", Order = 1, WeightPercent = 100m, Description = (string?)null });
        Assert.Equal(HttpStatusCode.OK, stageResponse.StatusCode);
        Guid stageId = (await stageResponse.Content.ReadFromJsonAsync<StageIdBody>())!.StageId;

        HttpResponseMessage taskResponse = await client.PostAsJsonAsync(
            $"/api/stages/{stageId}/tasks",
            new { Name = "Only Task", Order = 1, Weight = 1.00m, Description = (string?)null, DepartmentId = (Guid?)null });
        Assert.Equal(HttpStatusCode.OK, taskResponse.StatusCode);

        Guid clientId = await CrmTestSeeding.SeedClientAsync(fixture.ConnectionString, appUserId);
        HttpResponseMessage projectResponse = await client.PostAsJsonAsync("/api/projects", new
        {
            Code = $"P-{suffix}",
            Name = "Schema Isolation Test",
            Address = "1 Test Rd",
            WorkflowType = 1,
            ClientId = clientId,
            StageTemplateId = templateId,
        });
        Assert.Equal(HttpStatusCode.OK, projectResponse.StatusCode);
        Guid projectId = (await projectResponse.Content.ReadFromJsonAsync<ProjectIdBody>())!.ProjectId;

        // Only the query under test is observed - setup above legitimately touches identity./hr. (real
        // registration, real employee creation) and would make the assertion meaningless if included.
        fixture.ClearCapturedSql();

        HttpResponseMessage tasksResponse = await client.GetAsync($"/api/projects/{projectId}/tasks");
        Assert.Equal(HttpStatusCode.OK, tasksResponse.StatusCode);

        // PC-02 liveness - see CrmSchemaIsolationTests for why. The tasks read must query projects.;
        // if it did not, the negative assertion below would be measuring an empty capture.
        Assert.Contains(fixture.CapturedSql, sql => sql.Contains("projects.", StringComparison.OrdinalIgnoreCase));

        var touchedOtherSchemas = fixture.CapturedSql
            .Where(sql =>
                sql.Contains("identity.", StringComparison.OrdinalIgnoreCase) ||
                sql.Contains("hr.", StringComparison.OrdinalIgnoreCase) ||
                sql.Contains("approvals.", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(touchedOtherSchemas.Count == 0,
            $"GET .../tasks issued SQL touching identity/hr/approvals: {string.Join(" | ", touchedOtherSchemas)}");
    }

    private sealed record IdBody(Guid StageTemplateId);
    private sealed record StageIdBody(Guid StageId);
    private sealed record ProjectIdBody(Guid ProjectId);
}
