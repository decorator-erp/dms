using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Crm.Contracts;
using Npgsql;
using Xunit;

namespace Integration.Tests;

/// <summary>M1 (CRM production review): an artifact link's author is the caller, resolved on the server.
/// A body that names another employee or a contact as the author changes nothing — those fields are not
/// part of the request any more.</summary>
[Collection(ApiCollection.Name)]
public sealed class CrmArtifactAuthorTests(ApiFixture fixture)
{
    [Fact]
    public async Task M1_TheLinkBody_CannotSetTheAuthor_TheStoredAuthorIsTheCallingEmployee()
    {
        string suffix = Guid.NewGuid().ToString("N");
        (Guid employeeId, string employeeUserId) = await HrTestSeeding.HireEmployeeOverHttpAsync(fixture, $"artifact-author-{suffix}@example.com");
        HttpClient office = fixture.CreateClient();
        office.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fixture.IssueTestToken(
            employeeUserId, "project.create", "project.read_all", "stages.read", "stages.manage",
            "clients.create", "clients.read", "crm.briefs.manage", CrmPermissions.Artifacts.Manage, CrmPermissions.Artifacts.Read));
        (Guid projectId, Guid briefId) = await SeedProjectWithBriefAsync(office, suffix);
        Guid documentId = await SeedProjectDocumentAsync(projectId, employeeId);

        HttpResponseMessage linked = await office.PostAsJsonAsync("/api/crm/artifacts", new
        {
            documentId,
            briefId,
            addedByEmployeeId = Guid.NewGuid(),
        });
        Assert.Equal(HttpStatusCode.Created, linked.StatusCode);

        List<ArtifactBody> artifacts = (await office.GetFromJsonAsync<List<ArtifactBody>>($"/api/crm/artifacts/by-brief/{briefId}"))!;
        ArtifactBody artifact = Assert.Single(artifacts);
        Assert.Equal(employeeId, artifact.AddedByEmployeeId);
        Assert.Null(artifact.AddedByContactId);
    }

    /// <summary>A client, a one-stage template, a project and its brief, all over HTTP.</summary>
    internal static async Task<(Guid ProjectId, Guid BriefId)> SeedProjectWithBriefAsync(HttpClient office, string suffix)
    {
        HttpResponseMessage clientResponse = await office.PostAsJsonAsync("/api/clients", ClientRequests.NewClientBody("artifacts"));
        Assert.Equal(HttpStatusCode.Created, clientResponse.StatusCode);
        Guid clientId = (await clientResponse.Content.ReadFromJsonAsync<ClientIdBody>())!.ClientId;

        Guid templateId = (await (await office.PostAsJsonAsync("/api/stage-templates", new { Name = $"Tmpl-{suffix}", Type = 2 }))
            .Content.ReadFromJsonAsync<TemplateIdBody>())!.StageTemplateId;
        Assert.True((await office.PostAsJsonAsync(
            $"/api/stage-templates/{templateId}/stages",
            new { Name = "Design", Order = 1, WeightPercent = 100m, Description = (string?)null })).IsSuccessStatusCode);
        HttpResponseMessage projectResponse = await office.PostAsJsonAsync("/api/projects", new
        {
            Code = $"P-{suffix}",
            Name = "Artifact Links",
            Address = "1 Test Rd",
            WorkflowType = 1,
            ClientId = clientId,
            StageTemplateId = templateId,
        });
        Assert.Equal(HttpStatusCode.OK, projectResponse.StatusCode);
        Guid projectId = (await projectResponse.Content.ReadFromJsonAsync<ProjectIdBody>())!.ProjectId;
        Guid stageId = Assert.Single((await office.GetFromJsonAsync<List<StageIdBody>>($"/api/projects/{projectId}/stages"))!).Id;

        HttpResponseMessage briefResponse = await office.PostAsJsonAsync($"/api/crm/projects/{projectId}/brief", new
        {
            ProjectStageId = stageId,
            Data = new { UnitType = 1, UnitCondition = 1 },
        });
        Assert.Equal(HttpStatusCode.Created, briefResponse.StatusCode);
        return (projectId, (await briefResponse.Content.ReadFromJsonAsync<BriefIdBody>())!.BriefId);
    }

    /// <summary>A document row for the project, by SQL: the link only needs it to resolve through
    /// <c>IDocumentQueries.GetAsync</c> (same reasoning as <c>PeriodItemArtifactFlowTests</c>).</summary>
    internal async Task<Guid> SeedProjectDocumentAsync(Guid projectId, Guid uploadedById)
    {
        var documentId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO documents.project_documents
                ("Id", "ProjectId", "FileName", "StoragePath", "ContentType", "FileSizeBytes",
                 "FileExtension", "UploadedById", "UploadedOnUtc", "Version", "RootDocumentId",
                 "IsLocked", "IsActive", "CreatedAtUtc")
            VALUES
                (@id, @projectId, 'brief-photo.jpg', 'projects/x/brief-photo.jpg', 'image/jpeg', 1024,
                 '.jpg', @uploadedById, now(), 1, @id, false, true, now());
            """;
        command.Parameters.AddWithValue("id", documentId);
        command.Parameters.AddWithValue("projectId", projectId);
        command.Parameters.AddWithValue("uploadedById", uploadedById);
        await command.ExecuteNonQueryAsync();
        return documentId;
    }

    private sealed record ArtifactBody(Guid Id, Guid DocumentId, Guid? BriefId, Guid? AddedByEmployeeId, Guid? AddedByContactId);
    private sealed record ClientIdBody(Guid ClientId);
    private sealed record TemplateIdBody(Guid StageTemplateId);
    private sealed record ProjectIdBody(Guid ProjectId);
    private sealed record StageIdBody(Guid Id);
    private sealed record BriefIdBody(Guid BriefId);
}
