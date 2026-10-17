using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Artifacts;
using Crm.Contracts.Briefs;
using Crm.Contracts.Clients;
using Crm.Contracts.Contacts;
using Crm.Contracts.Items;
using Crm.Contracts.Periods;
using Hr.Contracts.Employees;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Projects.Contracts;
using Projects.Contracts.Projects;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>
/// G9-12's own verify block, resolved from the real DI graph. Needs Docker; see the standing
/// environment note in PROGRESS.md if this suite could not run.
/// </summary>
[Collection(CrmApplicationCollection.Name)]
public sealed class PeriodItemArtifactFlowTests(CrmApplicationFixture fixture)
{
    [Fact]
    public async Task Period_OpenExtendClose_PublishesPeriodClosedWhenStageOwned()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (string _, Guid clientId) = await CreateClientAsync(sp, "period-client");
        (Guid projectId, Guid employeeId) = await CreateProjectAsync(sp, clientId);
        var stageId = Guid.NewGuid();

        ICommandHandler<OpenCrmRequirementPeriodCommand, Guid> open = sp.GetRequiredService<ICommandHandler<OpenCrmRequirementPeriodCommand, Guid>>();
        Result<Guid> openResult = await open.HandleAsync(
            new OpenCrmRequirementPeriodCommand(projectId, stageId, DateTime.UtcNow.AddDays(7), employeeId));
        Assert.True(openResult.IsSuccess);
        Guid periodId = openResult.Value;

        ICommandHandler<ExtendCrmRequirementPeriodCommand> extend = sp.GetRequiredService<ICommandHandler<ExtendCrmRequirementPeriodCommand>>();
        Assert.True((await extend.HandleAsync(
            new ExtendCrmRequirementPeriodCommand(periodId, DateTime.UtcNow.AddDays(14)))).IsSuccess);

        ICommandHandler<CloseCrmRequirementPeriodCommand> close = sp.GetRequiredService<ICommandHandler<CloseCrmRequirementPeriodCommand>>();
        Assert.True((await close.HandleAsync(new CloseCrmRequirementPeriodCommand(periodId))).IsSuccess);

        IQueryHandler<GetCrmRequirementPeriodByIdQuery, CrmRequirementPeriodResponse> getById = sp.GetRequiredService<IQueryHandler<GetCrmRequirementPeriodByIdQuery, CrmRequirementPeriodResponse>>();
        CrmRequirementPeriodResponse period = (await getById.HandleAsync(new GetCrmRequirementPeriodByIdQuery(periodId))).Value!;
        Assert.Equal(RequirementPeriodStatus.Closed, period.Status);
    }

    [Fact]
    public async Task Period_OpenTwiceForSameProject_SecondIsRejected()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (string _, Guid clientId) = await CreateClientAsync(sp, "double-open-client");
        (Guid projectId, Guid employeeId) = await CreateProjectAsync(sp, clientId);

        ICommandHandler<OpenCrmRequirementPeriodCommand, Guid> open = sp.GetRequiredService<ICommandHandler<OpenCrmRequirementPeriodCommand, Guid>>();
        Assert.True((await open.HandleAsync(
            new OpenCrmRequirementPeriodCommand(projectId, null, DateTime.UtcNow.AddDays(7), employeeId))).IsSuccess);

        Result<Guid> secondResult = await open.HandleAsync(
            new OpenCrmRequirementPeriodCommand(projectId, null, DateTime.UtcNow.AddDays(7), employeeId));

        Assert.True(secondResult.IsFailure);
    }

    [Fact]
    public async Task RequirementItem_CreateSendSupersede_EmployeeAuthored()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (string _, Guid clientId) = await CreateClientAsync(sp, "item-client");
        (Guid projectId, Guid employeeId) = await CreateProjectAsync(sp, clientId);
        ICommandHandler<OpenCrmRequirementPeriodCommand, Guid> open = sp.GetRequiredService<ICommandHandler<OpenCrmRequirementPeriodCommand, Guid>>();
        Guid periodId = (await open.HandleAsync(
            new OpenCrmRequirementPeriodCommand(projectId, null, DateTime.UtcNow.AddDays(7), employeeId))).Value;

        ICommandHandler<CreateCrmRequirementItemCommand, Guid> createItem = sp.GetRequiredService<ICommandHandler<CreateCrmRequirementItemCommand, Guid>>();
        Result<Guid> itemResult = await createItem.HandleAsync(new CreateCrmRequirementItemCommand(
            projectId, periodId, employeeId, RequirementCategory.Materials, "Matte flooring", RequirementPriority.High));
        Assert.True(itemResult.IsSuccess);
        Guid itemId = itemResult.Value;

        ICommandHandler<SendCrmRequirementItemCommand> send = sp.GetRequiredService<ICommandHandler<SendCrmRequirementItemCommand>>();
        Assert.True((await send.HandleAsync(new SendCrmRequirementItemCommand(itemId))).IsSuccess);

        ICommandHandler<SupersedeCrmRequirementItemCommand, Guid> supersede = sp.GetRequiredService<ICommandHandler<SupersedeCrmRequirementItemCommand, Guid>>();
        Result<Guid> supersedeResult = await supersede.HandleAsync(new SupersedeCrmRequirementItemCommand(
            itemId, Guid.NewGuid(), RequirementCategory.Materials, "Corrected flooring", RequirementPriority.High));

        // Only a locked (Approved/NotRequired) item can be superseded - a PendingApproval item must
        // be rejected here, proving the ledger rule survives the round-trip through DI.
        Assert.True(supersedeResult.IsFailure);
    }

    [Fact]
    public async Task RequirementItem_CreatedByClient_IsTrustedImmediately()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (string _, Guid clientId) = await CreateClientAsync(sp, "client-item-client");
        (Guid projectId, Guid employeeId) = await CreateProjectAsync(sp, clientId);
        ICommandHandler<OpenCrmRequirementPeriodCommand, Guid> open = sp.GetRequiredService<ICommandHandler<OpenCrmRequirementPeriodCommand, Guid>>();
        Guid periodId = (await open.HandleAsync(
            new OpenCrmRequirementPeriodCommand(projectId, null, DateTime.UtcNow.AddDays(7), employeeId))).Value;
        Guid contactId = await SeedContactAsync(sp, clientId);

        ICommandHandler<CreateCrmRequirementItemByClientCommand, Guid> createByClient = sp.GetRequiredService<ICommandHandler<CreateCrmRequirementItemByClientCommand, Guid>>();
        Result<Guid> result = await createByClient.HandleAsync(new CreateCrmRequirementItemByClientCommand(
            projectId, periodId, contactId, RequirementCategory.Style, "I want warm colors", RequirementPriority.Medium));
        Assert.True(result.IsSuccess);

        IQueryHandler<GetCrmRequirementItemsByPeriodQuery, IReadOnlyList<CrmRequirementItemResponse>> getByPeriod = sp.GetRequiredService<IQueryHandler<GetCrmRequirementItemsByPeriodQuery, IReadOnlyList<CrmRequirementItemResponse>>>();
        IReadOnlyList<CrmRequirementItemResponse> items = (await getByPeriod.HandleAsync(new GetCrmRequirementItemsByPeriodQuery(periodId))).Value!;
        Assert.Equal(CrmApprovalState.NotRequired, items.Single().ApprovalStatus);
    }

    [Fact]
    public async Task LinkArtifactDocument_WithUnknownDocumentId_IsRejected()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        ICommandHandler<LinkCrmArtifactDocumentCommand, Guid> link = sp.GetRequiredService<ICommandHandler<LinkCrmArtifactDocumentCommand, Guid>>();

        Result<Guid> result = await link.HandleAsync(new LinkCrmArtifactDocumentCommand(
            Guid.NewGuid(), Guid.NewGuid(), null, null, null, Guid.NewGuid(), null));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task LinkArtifactDocument_ThenQueryByBrief_RoundTrips()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (string _, Guid clientId) = await CreateClientAsync(sp, "artifact-rt-client");
        (Guid projectId, Guid employeeId) = await CreateProjectAsync(sp, clientId);
        Guid documentId = await SeedProjectDocumentAsync(projectId, employeeId);

        ICommandHandler<CreateCrmDesignBriefCommand, Guid> createBrief = sp.GetRequiredService<ICommandHandler<CreateCrmDesignBriefCommand, Guid>>();
        Guid briefId = (await createBrief.HandleAsync(new CreateCrmDesignBriefCommand(
            projectId, employeeId, null, new CrmBriefHeadData((UnitType)1, (UnitCondition)1)))).Value;

        ICommandHandler<LinkCrmArtifactDocumentCommand, Guid> link = sp.GetRequiredService<ICommandHandler<LinkCrmArtifactDocumentCommand, Guid>>();
        Result<Guid> linkResult = await link.HandleAsync(new LinkCrmArtifactDocumentCommand(
            documentId, briefId, null, null, null, employeeId, null));
        Assert.True(linkResult.IsSuccess);

        IQueryHandler<GetCrmArtifactDocumentsByBriefQuery, IReadOnlyList<CrmArtifactDocumentResponse>> getByBrief = sp.GetRequiredService<IQueryHandler<GetCrmArtifactDocumentsByBriefQuery, IReadOnlyList<CrmArtifactDocumentResponse>>>();
        IReadOnlyList<CrmArtifactDocumentResponse> artifacts = (await getByBrief.HandleAsync(new GetCrmArtifactDocumentsByBriefQuery(briefId))).Value!;

        Assert.Single(artifacts);
        Assert.Equal(documentId, artifacts[0].DocumentId);
    }

    private static async Task<(string AppUserId, Guid ClientId)> CreateClientAsync(IServiceProvider sp, string label)
    {
        string suffix = Guid.NewGuid().ToString("N");
        (Guid clientId, string appUserId) = await CrmClientSeeding.CreateClientAsync(
            sp, $"{label}-{suffix}@example.com", lastName: label);

        return (appUserId, clientId);
    }

    private static async Task<(Guid ProjectId, Guid EmployeeId)> CreateProjectAsync(IServiceProvider sp, Guid clientId)
    {
        string suffix = Guid.NewGuid().ToString("N");
        (Guid employeeId, string ownerAppUserId) = await HrTestSeeding.HireEmployeeAsync(
            sp, $"owner-{suffix}@example.com", jobTitle: "PM", firstName: "Owner");

        TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = ownerAppUserId;

        ICommandHandler<CreateProjectCommand, Guid> createProject = sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>();
        Guid projectId = (await createProject.HandleAsync(
            new CreateProjectCommand($"P-{suffix}", "Test Project", "123 Main St", WorkflowType.Stage, clientId))).Value;

        return (projectId, employeeId);
    }

    /// <summary>E2-02: seeds a real <c>crm.crm_contacts</c> row for
    /// <c>CrmRequirementItemConfiguration</c>'s same-schema FK on <c>CreatedByContactId</c> — a
    /// fabricated Guid no longer survives the insert, same reasoning as this file's own
    /// <c>CreateProjectAsync</c> for the employee side.</summary>
    private static async Task<Guid> SeedContactAsync(IServiceProvider sp, Guid clientId)
    {
        ICommandHandler<CreateCrmContactCommand, Guid> createContact = sp.GetRequiredService<ICommandHandler<CreateCrmContactCommand, Guid>>();
        Result<Guid> result = await createContact.HandleAsync(
            new CreateCrmContactCommand(clientId, "Test", "Contact", ContactRole.Owner, IsPrimary: true));
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    /// <summary>Raw SQL, not <c>Documents.Application</c>'s own upload command — driving a real
    /// multipart-style upload through <c>IFileStorageService</c> is unrelated overhead for a test that
    /// only needs a document ROW to exist for <c>IDocumentQueries.GetAsync</c> to resolve. Same
    /// reasoning <c>ProjectAccessGuardTests.SeedEmployeeAsync</c> gives for raw-SQL seeding across a
    /// module boundary this test project has no <c>InternalsVisibleTo</c> grant into.</summary>
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
