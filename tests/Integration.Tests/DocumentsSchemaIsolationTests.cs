using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Identity.Contracts.Auth;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// G7-08's schema-isolation assertion — same shape <c>ProjectsSchemaIsolationTests</c> established at
/// G5-10, one module wider: a Documents-owned read issues no SQL touching <c>identity.</c>/<c>hr.</c>/
/// <c>approvals.</c>/<c>projects.</c> — <c>GetProjectDocumentByIdQueryHandler</c> is a single
/// <c>DocumentsDbContext</c> projection with no cross-module lookup at all, so this is a stronger claim
/// than Projects' own version (which only reaches its ReadAll bypass under a specific permission).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DocumentsSchemaIsolationTests(ApiFixture fixture)
{
    [Fact]
    public async Task GetDocumentById_IssuesNoSqlTouchingIdentityHrApprovalsOrProjectsSchemas()
    {
        var client = fixture.CreateClient();
        var suffix = Guid.NewGuid().ToString("N");

        var registerResponse = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            $"doc-schema-{suffix}@example.com", "P@ssw0rd123!", "Test", "User", null, "employee"));
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        var appUserId = (await registerResponse.Content.ReadFromJsonAsync<RegisterResponseBody>())!.UserId;

        var token = fixture.IssueTestToken(
            appUserId, "employees.create", "stages.manage", "project.create", "documents.upload", "documents.read");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var employeeResponse = await client.PostAsJsonAsync("/api/employees", new
        {
            AppUserId = appUserId,
            JobTitle = "Engineer",
            HireDate = DateOnly.FromDateTime(DateTime.UtcNow),
            ContractType = "FullTime",
        });
        Assert.Equal(HttpStatusCode.OK, employeeResponse.StatusCode);

        // project_documents.project_id carries a real cross-schema FK to projects.projects (G7-04) - a
        // fabricated project id would fail the upload outright, so a real project is needed here even
        // though this test's own assertion only cares about the GET afterward.
        var templateResponse = await client.PostAsJsonAsync("/api/stage-templates", new { Name = $"Tmpl-{suffix}", Type = 2 });
        Assert.Equal(HttpStatusCode.OK, templateResponse.StatusCode);
        var templateId = (await templateResponse.Content.ReadFromJsonAsync<IdBody>())!.StageTemplateId;

        var stageResponse = await client.PostAsJsonAsync(
            $"/api/stage-templates/{templateId}/stages",
            new { Name = "Only Stage", Order = 1, WeightPercent = 100m, Description = (string?)null });
        Assert.Equal(HttpStatusCode.OK, stageResponse.StatusCode);

        var projectResponse = await client.PostAsJsonAsync("/api/projects", new
        {
            Code = $"P-{suffix}",
            Name = "Schema Isolation Test",
            Address = "1 Test Rd",
            WorkflowType = 1,
            ClientId = Guid.NewGuid(),
            StageTemplateId = templateId,
        });
        Assert.Equal(HttpStatusCode.OK, projectResponse.StatusCode);
        var projectId = (await projectResponse.Content.ReadFromJsonAsync<ProjectIdBody>())!.ProjectId;

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("schema isolation"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "doc.txt");

        var uploadResponse = await client.PostAsync($"/api/projects/{projectId}/documents", content);
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        var documentId = (await uploadResponse.Content.ReadFromJsonAsync<DocumentIdBody>())!.DocumentId;

        // Only the query under test is observed - setup above legitimately touches identity./hr./projects.
        // and would make the assertion meaningless if included.
        fixture.ClearCapturedSql();

        var getResponse = await client.GetAsync($"/api/project-documents/{documentId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var touchedOtherSchemas = fixture.CapturedSql
            .Where(sql =>
                sql.Contains("identity.", StringComparison.OrdinalIgnoreCase) ||
                sql.Contains("hr.", StringComparison.OrdinalIgnoreCase) ||
                sql.Contains("approvals.", StringComparison.OrdinalIgnoreCase) ||
                sql.Contains("projects.", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(touchedOtherSchemas.Count == 0,
            $"GET .../project-documents/{{id}} issued SQL touching identity/hr/approvals/projects: {string.Join(" | ", touchedOtherSchemas)}");
    }

    private sealed record RegisterResponseBody(string UserId);
    private sealed record IdBody(Guid StageTemplateId);
    private sealed record ProjectIdBody(Guid ProjectId);
    private sealed record DocumentIdBody(Guid DocumentId);
}
