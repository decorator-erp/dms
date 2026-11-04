using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Artifacts;
using Crm.Contracts.Briefs;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>L-4 (CRM production review): a document is linked to a subject once — the second link is 409
/// <c>crm.artifacts.AlreadyLinked</c>, backed by a partial unique index per subject column — and the
/// by-subject lists are capped at <see cref="MaxListed"/> rows in a stable order.</summary>
[Collection(CrmApplicationCollection.Name)]
public sealed class ArtifactLinkUniquenessTests(CrmApplicationFixture fixture)
{
    private const int MaxListed = 200;

    [Fact]
    public async Task L4_LinkingTheSameDocumentToTheSameBriefTwice_Is409_AndLeavesOneLink()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "l4-dup");
        Guid briefId = await CreateBriefAsync(sp, p);
        Guid documentId = await SeedProjectDocumentAsync(p.ProjectId, p.EmployeeId);

        Assert.True((await LinkAsync(p, documentId, briefId)).IsSuccess);
        Result<Guid> second = await LinkAsync(p, documentId, briefId);

        Assert.True(second.IsFailure, "Expected a 409, got success.");
        Assert.Equal((409, "crm.artifacts.AlreadyLinked"), (second.Error.Status, second.Error.Code));
        Assert.Single(await ListByBriefAsync(p, briefId));
    }

    [Fact]
    public async Task L4_TheSameDocument_MayStillBeLinkedToTwoSubjects()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "l4-two", stageCount: 2);
        Guid firstBrief = await CreateBriefAsync(sp, p);
        Guid secondBrief = await CreateBriefAsync(sp, p, p.StageIds[1]);
        Guid documentId = await SeedProjectDocumentAsync(p.ProjectId, p.EmployeeId);

        Assert.True((await LinkAsync(p, documentId, firstBrief)).IsSuccess);
        Assert.True((await LinkAsync(p, documentId, secondBrief)).IsSuccess);
    }

    [Fact]
    public async Task L4_TheByBriefList_IsCappedAt200_OldestFirst()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "l4-cap");
        Guid briefId = await CreateBriefAsync(sp, p);

        // 201 links, by SQL (DocumentId carries no FK); creation times one second apart so the order is known.
        await using (var connection = new NpgsqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO crm.crm_artifact_documents ("Id", "DocumentId", "BriefId", "AddedByEmployeeId", "IsActive", "CreatedAtUtc")
                SELECT gen_random_uuid(), gen_random_uuid(), @briefId, @employeeId, true, now() - make_interval(secs => 1000 - n)
                FROM generate_series(1, 201) AS n;
                """;
            command.Parameters.AddWithValue("briefId", briefId);
            command.Parameters.AddWithValue("employeeId", p.EmployeeId);
            Assert.Equal(201, await command.ExecuteNonQueryAsync());
        }

        IReadOnlyList<CrmArtifactDocumentResponse> first = await ListByBriefAsync(p, briefId);
        IReadOnlyList<CrmArtifactDocumentResponse> again = await ListByBriefAsync(p, briefId);

        Assert.Equal(MaxListed, first.Count);
        Assert.Equal(first.Select(a => a.Id), again.Select(a => a.Id));
    }

    private static async Task<Guid> CreateBriefAsync(IServiceProvider sp, BriefTestProject p, Guid? stageId = null)
    {
        Result<Guid> brief = await sp.GetRequiredService<ICommandHandler<CreateCrmDesignBriefCommand, Guid>>().HandleAsync(
            new CreateCrmDesignBriefCommand(p.ProjectId, p.EmployeeId, stageId ?? p.StageId,
                new CrmBriefHeadData((UnitType)1, (UnitCondition)1), p.OwnerAppUserId));
        Assert.True(brief.IsSuccess, brief.Error?.Description);
        return brief.Value;
    }

    private async Task<Result<Guid>> LinkAsync(BriefTestProject p, Guid documentId, Guid briefId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<LinkCrmArtifactDocumentCommand, Guid>>()
            .HandleAsync(new LinkCrmArtifactDocumentCommand(documentId, briefId, null, null, null, p.EmployeeId, null, p.OwnerAppUserId));
    }

    private async Task<IReadOnlyList<CrmArtifactDocumentResponse>> ListByBriefAsync(BriefTestProject p, Guid briefId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        Result<IReadOnlyList<CrmArtifactDocumentResponse>> list = await scope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetCrmArtifactDocumentsByBriefQuery, IReadOnlyList<CrmArtifactDocumentResponse>>>()
            .HandleAsync(new GetCrmArtifactDocumentsByBriefQuery(briefId, p.OwnerAppUserId));
        Assert.True(list.IsSuccess, list.Error?.Description);
        return list.Value!;
    }

    private async Task<Guid> SeedProjectDocumentAsync(Guid projectId, Guid uploadedById)
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
}
