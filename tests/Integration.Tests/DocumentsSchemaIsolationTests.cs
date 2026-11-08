using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BuildingBlocks.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// G7-08's schema-isolation assertion — same shape <c>ProjectsSchemaIsolationTests</c> established at
/// G5-10, one module wider: a Documents-owned read issues no SQL touching <c>identity.</c>/<c>hr.</c>/
/// <c>approvals.</c>/<c>projects.</c> — <c>GetProjectDocumentByIdQueryHandler</c> is a single
/// <c>DocumentsDbContext</c> projection with no cross-module lookup at all.
///
/// <para><b>BF-1: the caller carries <c>project.read_all</c>, as Projects' own version does.</b> Documents is
/// project-scoped now, so a read first asks <c>IProjectAccessGuard</c> — a Projects contract, which reads
/// Projects' (and, for the caller's employee id, HR's) tables on Projects' side of the boundary, legitimately.
/// <c>read_all</c> is the guard's bypass and is read off the caller without SQL, which leaves exactly what this
/// test is about: Documents' own read touches no other schema. The member path through the guard is covered by
/// <c>DocumentsScopeRouteTests</c>.</para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DocumentsSchemaIsolationTests(ApiFixture fixture)
{
    [Fact]
    public async Task GetDocumentById_IssuesNoSqlTouchingIdentityHrApprovalsOrProjectsSchemas()
    {
        HttpClient client = fixture.CreateClient();
        string suffix = Guid.NewGuid().ToString("N");

        (_, string appUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(
            fixture, $"doc-schema-{suffix}@example.com");

        string token = fixture.IssueTestToken(
            appUserId, "employees.create", "stages.manage", "project.create", "documents.upload", "documents.read",
            "project.read_all");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // project_documents.project_id carries a real cross-schema FK to projects.projects (G7-04) - a
        // fabricated project id would fail the upload outright, so a real project is needed here even
        // though this test's own assertion only cares about the GET afterward.
        HttpResponseMessage templateResponse = await client.PostAsJsonAsync("/api/stage-templates", new { Name = $"Tmpl-{suffix}", Type = 2 });
        Assert.Equal(HttpStatusCode.OK, templateResponse.StatusCode);
        Guid templateId = (await templateResponse.Content.ReadFromJsonAsync<IdBody>())!.StageTemplateId;

        HttpResponseMessage stageResponse = await client.PostAsJsonAsync(
            $"/api/stage-templates/{templateId}/stages",
            new { Name = "Only Stage", Order = 1, WeightPercent = 100m, Description = (string?)null });
        Assert.Equal(HttpStatusCode.OK, stageResponse.StatusCode);

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

        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent("schema isolation"u8.ToArray());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(fileContent, "file", "doc.txt");

        HttpResponseMessage uploadResponse = await client.PostAsync($"/api/projects/{projectId}/documents", content);
        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        Guid documentId = (await uploadResponse.Content.ReadFromJsonAsync<DocumentIdBody>())!.DocumentId;

        // Drain the outbox to quiescence BEFORE measuring. Uploading publishes
        // DocumentUploadedIntegrationEvent, and Projects' handler for it queries
        // projects.gate_conditions to satisfy Attachment gates - legitimately, in ITS own module. But
        // CapturedSql is process-global and cannot attribute a statement to the request that caused it,
        // so a background delivery landing inside the measured window reads exactly like a leak from
        // this GET.
        //
        // N1-08 found this: CapturedSql had never captured anything (UseSerilog was replacing the
        // provider), so this assertion had always passed vacuously. With capture working it failed
        // immediately, on SQL the background processor issued. Draining first removes the race rather
        // than widening the filter to hide it.
        OutboxProcessor outboxProcessor = fixture.Services.GetServices<IHostedService>().OfType<OutboxProcessor>().Single();
        await outboxProcessor.ProcessPendingAsync(CancellationToken.None);

        // Only the query under test is observed - setup above legitimately touches identity./hr./projects.
        // and would make the assertion meaningless if included.
        fixture.ClearCapturedSql();

        HttpResponseMessage getResponse = await client.GetAsync($"/api/project-documents/{documentId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        // PC-02 liveness. This file already documents that N1-08 found the collector dead; the comment
        // above records the failure, this line makes the test itself detect a recurrence.
        Assert.Contains(fixture.CapturedSql, sql => sql.Contains("documents.", StringComparison.OrdinalIgnoreCase));

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

    private sealed record IdBody(Guid StageTemplateId);
    private sealed record ProjectIdBody(Guid ProjectId);
    private sealed record DocumentIdBody(Guid DocumentId);
}
