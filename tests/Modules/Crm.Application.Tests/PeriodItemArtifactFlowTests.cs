using BuildingBlocks.Messaging;
using BuildingBlocks.Pagination;
using Crm.Contracts;
using Crm.Contracts.Artifacts;
using Crm.Contracts.Briefs;
using Crm.Contracts.Clients;
using Crm.Contracts.Contacts;
using Crm.Contracts.Items;
using Crm.Contracts.Periods;
using Crm.Contracts.Spaces;
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
        // A-2: the stage must be a real stage of this project, so seed a project with one.
        BriefTestProject seeded = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "period-client");
        (Guid projectId, Guid employeeId, Guid stageId) = (seeded.ProjectId, seeded.EmployeeId, seeded.StageId);

        ICommandHandler<OpenCrmRequirementPeriodCommand, Guid> open = sp.GetRequiredService<ICommandHandler<OpenCrmRequirementPeriodCommand, Guid>>();
        Result<Guid> openResult = await open.HandleAsync(
            new OpenCrmRequirementPeriodCommand(projectId, stageId, DateTime.UtcNow.AddDays(7), employeeId, Me(sp)));
        Assert.True(openResult.IsSuccess);
        Guid periodId = openResult.Value;

        ICommandHandler<ExtendCrmRequirementPeriodCommand> extend = sp.GetRequiredService<ICommandHandler<ExtendCrmRequirementPeriodCommand>>();
        Assert.True((await extend.HandleAsync(
            new ExtendCrmRequirementPeriodCommand(periodId, DateTime.UtcNow.AddDays(14), Me(sp)))).IsSuccess);

        ICommandHandler<CloseCrmRequirementPeriodCommand> close = sp.GetRequiredService<ICommandHandler<CloseCrmRequirementPeriodCommand>>();
        Assert.True((await close.HandleAsync(new CloseCrmRequirementPeriodCommand(periodId, Me(sp)))).IsSuccess);

        IQueryHandler<GetCrmRequirementPeriodByIdQuery, CrmRequirementPeriodResponse> getById = sp.GetRequiredService<IQueryHandler<GetCrmRequirementPeriodByIdQuery, CrmRequirementPeriodResponse>>();
        CrmRequirementPeriodResponse period = (await getById.HandleAsync(new GetCrmRequirementPeriodByIdQuery(periodId, Me(sp)))).Value!;
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
            new OpenCrmRequirementPeriodCommand(projectId, null, DateTime.UtcNow.AddDays(7), employeeId, Me(sp)))).IsSuccess);

        Result<Guid> secondResult = await open.HandleAsync(
            new OpenCrmRequirementPeriodCommand(projectId, null, DateTime.UtcNow.AddDays(7), employeeId, Me(sp)));

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
            new OpenCrmRequirementPeriodCommand(projectId, null, DateTime.UtcNow.AddDays(7), employeeId, Me(sp)))).Value;

        ICommandHandler<CreateCrmRequirementItemCommand, Guid> createItem = sp.GetRequiredService<ICommandHandler<CreateCrmRequirementItemCommand, Guid>>();
        Result<Guid> itemResult = await createItem.HandleAsync(new CreateCrmRequirementItemCommand(
            projectId, periodId, employeeId, RequirementCategory.Materials, "Matte flooring", RequirementPriority.High, Me(sp)));
        Assert.True(itemResult.IsSuccess);
        Guid itemId = itemResult.Value;

        ICommandHandler<SendCrmRequirementItemCommand> send = sp.GetRequiredService<ICommandHandler<SendCrmRequirementItemCommand>>();
        Assert.True((await send.HandleAsync(new SendCrmRequirementItemCommand(itemId, sp.GetRequiredService<TestCurrentUser>().UserId!))).IsSuccess);

        ICommandHandler<SupersedeCrmRequirementItemCommand, Guid> supersede = sp.GetRequiredService<ICommandHandler<SupersedeCrmRequirementItemCommand, Guid>>();
        Result<Guid> supersedeResult = await supersede.HandleAsync(new SupersedeCrmRequirementItemCommand(
            itemId, Guid.NewGuid(), RequirementCategory.Materials, "Corrected flooring", RequirementPriority.High, Me(sp)));

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
            new OpenCrmRequirementPeriodCommand(projectId, null, DateTime.UtcNow.AddDays(7), employeeId, Me(sp)))).Value;
        Guid contactId = await SeedContactAsync(sp, clientId);

        ICommandHandler<CreateCrmRequirementItemByClientCommand, Guid> createByClient = sp.GetRequiredService<ICommandHandler<CreateCrmRequirementItemByClientCommand, Guid>>();
        Result<Guid> result = await createByClient.HandleAsync(new CreateCrmRequirementItemByClientCommand(
            projectId, periodId, contactId, RequirementCategory.Style, "I want warm colors", RequirementPriority.Medium));
        Assert.True(result.IsSuccess);

        IQueryHandler<GetCrmRequirementItemsByPeriodQuery, PagedResult<CrmRequirementItemResponse>> getByPeriod = sp.GetRequiredService<IQueryHandler<GetCrmRequirementItemsByPeriodQuery, PagedResult<CrmRequirementItemResponse>>>();
        IReadOnlyList<CrmRequirementItemResponse> items = (await getByPeriod.HandleAsync(new GetCrmRequirementItemsByPeriodQuery(periodId, Me(sp)))).Value!.Items;
        Assert.Equal(CrmApprovalState.NotRequired, items.Single().ApprovalStatus);
    }

    /// <summary>X-1: a client may file an item only into an open period of its own project.</summary>
    [Fact]
    public async Task RequirementItem_ByClient_IsTenantChecked_AndNeedsAnOpenPeriodOfThatProject()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        PortalItemSetup own = await CreatePortalItemSetupAsync(sp, "x1-own");
        PortalItemSetup other = await CreatePortalItemSetupAsync(sp, "x1-other");
        ICommandHandler<CreateCrmRequirementItemByClientCommand, Guid> create = sp.GetRequiredService<ICommandHandler<CreateCrmRequirementItemByClientCommand, Guid>>();

        // Another client's project (with its own period) and an unknown project: 404.
        AssertError(404, "crm.portal.NotFound", await create.HandleAsync(Item(other.ProjectId, other.PeriodId, own.ContactId)));
        AssertError(404, "crm.portal.NotFound", await create.HandleAsync(Item(Guid.NewGuid(), own.PeriodId, own.ContactId)));

        // Own project, but another project's period: 404.
        AssertError(404, "crm.portal.NotFound", await create.HandleAsync(Item(own.ProjectId, other.PeriodId, own.ContactId)));

        // Own project and period, but closed: 409.
        Assert.True((await sp.GetRequiredService<ICommandHandler<CloseCrmRequirementPeriodCommand>>()
            .HandleAsync(new CloseCrmRequirementPeriodCommand(own.PeriodId, Me(sp)))).IsSuccess);
        AssertError(409, "crm.periods.NotOpen", await create.HandleAsync(Item(own.ProjectId, own.PeriodId, own.ContactId)));
    }

    /// <summary>X-1: the space type must exist and be active; the body is bounded like the columns.</summary>
    [Fact]
    public async Task RequirementItem_ByClient_RejectsAnUnknownOrInactiveSpaceType_AndAnInvalidBody()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        PortalItemSetup p = await CreatePortalItemSetupAsync(sp, "x1-body");
        ICommandHandler<CreateCrmRequirementItemByClientCommand, Guid> create = sp.GetRequiredService<ICommandHandler<CreateCrmRequirementItemByClientCommand, Guid>>();

        AssertError(400, "crm.items.UnknownSpaceType",
            await create.HandleAsync(Item(p.ProjectId, p.PeriodId, p.ContactId) with { SpaceTypeId = Guid.NewGuid() }));

        Guid spaceTypeId = (await sp.GetRequiredService<ICommandHandler<CreateCrmSpaceTypeCommand, Guid>>()
            .HandleAsync(new CreateCrmSpaceTypeCommand($"X1{Guid.NewGuid():N}"[..20], "Kitchen", "مطبخ"))).Value;
        Assert.True((await sp.GetRequiredService<ICommandHandler<SetCrmSpaceTypeActiveCommand>>()
            .HandleAsync(new SetCrmSpaceTypeActiveCommand(spaceTypeId, false))).IsSuccess);
        AssertError(400, "crm.items.UnknownSpaceType",
            await create.HandleAsync(Item(p.ProjectId, p.PeriodId, p.ContactId) with { SpaceTypeId = spaceTypeId }));

        Result<Guid> longTitle = await create.HandleAsync(Item(p.ProjectId, p.PeriodId, p.ContactId) with { Title = new string('t', 257) });
        Assert.Equal(400, longTitle.Error.Status);
        Result<Guid> longRoom = await create.HandleAsync(Item(p.ProjectId, p.PeriodId, p.ContactId) with { Room = new string('r', 129) });
        Assert.Equal(400, longRoom.Error.Status);
        Result<Guid> longDescription = await create.HandleAsync(Item(p.ProjectId, p.PeriodId, p.ContactId) with { Description = new string('d', 2001) });
        Assert.Equal(400, longDescription.Error.Status);
        Result<Guid> badCategory = await create.HandleAsync(Item(p.ProjectId, p.PeriodId, p.ContactId) with { Category = (RequirementCategory)999 });
        Assert.Equal(400, badCategory.Error.Status);
        Result<Guid> badPriority = await create.HandleAsync(Item(p.ProjectId, p.PeriodId, p.ContactId) with { Priority = (RequirementPriority)999 });
        Assert.Equal(400, badPriority.Error.Status);
    }

    [Fact]
    public async Task RequirementItem_ByClient_OnItsOwnOpenPeriod_WithAnActiveSpaceType_Succeeds()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        PortalItemSetup p = await CreatePortalItemSetupAsync(sp, "x1-happy");
        Guid spaceTypeId = (await sp.GetRequiredService<ICommandHandler<CreateCrmSpaceTypeCommand, Guid>>()
            .HandleAsync(new CreateCrmSpaceTypeCommand($"X1{Guid.NewGuid():N}"[..20], "Living", "معيشة"))).Value;

        Result<Guid> result = await sp.GetRequiredService<ICommandHandler<CreateCrmRequirementItemByClientCommand, Guid>>()
            .HandleAsync(Item(p.ProjectId, p.PeriodId, p.ContactId) with
            {
                SpaceTypeId = spaceTypeId,
                Room = new string('r', 128),
                Title = new string('t', 256),
                Description = new string('d', 2000),
            });

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
    }

    private sealed record PortalItemSetup(Guid ProjectId, Guid PeriodId, Guid ContactId);

    private static async Task<PortalItemSetup> CreatePortalItemSetupAsync(IServiceProvider sp, string label)
    {
        (string _, Guid clientId) = await CreateClientAsync(sp, label);
        (Guid projectId, Guid employeeId) = await CreateProjectAsync(sp, clientId);
        Guid periodId = (await sp.GetRequiredService<ICommandHandler<OpenCrmRequirementPeriodCommand, Guid>>()
            .HandleAsync(new OpenCrmRequirementPeriodCommand(projectId, null, DateTime.UtcNow.AddDays(7), employeeId, Me(sp)))).Value;
        return new PortalItemSetup(projectId, periodId, await SeedContactAsync(sp, clientId));
    }

    private static CreateCrmRequirementItemByClientCommand Item(Guid projectId, Guid periodId, Guid contactId) =>
        new(projectId, periodId, contactId, RequirementCategory.Style, "Warm colors", RequirementPriority.Medium);

    private static void AssertError(int status, string code, Result<Guid> result)
    {
        Assert.True(result.IsFailure, "Expected a failure, got success.");
        Assert.Equal((status, code), (result.Error.Status, result.Error.Code));
    }

    [Fact]
    public async Task LinkArtifactDocument_WithUnknownDocumentId_IsRejected()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        ICommandHandler<LinkCrmArtifactDocumentCommand, Guid> link = sp.GetRequiredService<ICommandHandler<LinkCrmArtifactDocumentCommand, Guid>>();

        Result<Guid> result = await link.HandleAsync(new LinkCrmArtifactDocumentCommand(
            Guid.NewGuid(), Guid.NewGuid(), null, null, null, Guid.NewGuid(), null, "someone"));

        Assert.True(result.IsFailure);
    }

    /// <summary>FINDING-P1-13 (adjacent to D-12): the link write is project-scoped on the subject it names, as
    /// the lookups are — an outsider gets 404 and nothing is linked.</summary>
    [Fact]
    public async Task LinkArtifactDocument_ByAnOutsider_Is404_AndLinksNothing()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "artifact-outsider");
        Guid documentId = await SeedProjectDocumentAsync(p.ProjectId, p.EmployeeId);
        Guid briefId = (await sp.GetRequiredService<ICommandHandler<CreateCrmDesignBriefCommand, Guid>>().HandleAsync(new CreateCrmDesignBriefCommand(
            p.ProjectId, p.EmployeeId, p.StageId, new CrmBriefHeadData((UnitType)1, (UnitCondition)1), p.OwnerAppUserId))).Value;
        (Guid outsiderId, string outsider) = await HrTestSeeding.HireEmployeeAsync(sp, $"artifact-outsider-{Guid.NewGuid():N}@example.com");

        Result<Guid> linked;
        await using (AsyncServiceScope outsiderScope = fixture.CreateScope())
        {
            // A fresh scope: the seeding above granted read_all on its own.
            outsiderScope.ServiceProvider.GetRequiredService<TestCurrentUser>().UserId = outsider;
            linked = await outsiderScope.ServiceProvider.GetRequiredService<ICommandHandler<LinkCrmArtifactDocumentCommand, Guid>>()
                .HandleAsync(new LinkCrmArtifactDocumentCommand(documentId, briefId, null, null, null, outsiderId, null, outsider));
        }

        AssertError(404, "crm.briefs.NotFound", linked);
        Assert.Empty((await sp.GetRequiredService<IQueryHandler<GetCrmArtifactDocumentsByBriefQuery, IReadOnlyList<CrmArtifactDocumentResponse>>>()
            .HandleAsync(new GetCrmArtifactDocumentsByBriefQuery(briefId, p.OwnerAppUserId))).Value!);
    }

    [Fact]
    public async Task LinkArtifactDocument_ThenQueryByBrief_RoundTrips()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(sp, "artifact-rt-client");
        Guid employeeId = p.EmployeeId;
        Guid documentId = await SeedProjectDocumentAsync(p.ProjectId, employeeId);

        ICommandHandler<CreateCrmDesignBriefCommand, Guid> createBrief = sp.GetRequiredService<ICommandHandler<CreateCrmDesignBriefCommand, Guid>>();
        Guid briefId = (await createBrief.HandleAsync(new CreateCrmDesignBriefCommand(
            p.ProjectId, employeeId, p.StageId, new CrmBriefHeadData((UnitType)1, (UnitCondition)1), p.OwnerAppUserId))).Value;

        ICommandHandler<LinkCrmArtifactDocumentCommand, Guid> link = sp.GetRequiredService<ICommandHandler<LinkCrmArtifactDocumentCommand, Guid>>();
        Result<Guid> linkResult = await link.HandleAsync(new LinkCrmArtifactDocumentCommand(
            documentId, briefId, null, null, null, employeeId, null, p.OwnerAppUserId));
        Assert.True(linkResult.IsSuccess);

        IQueryHandler<GetCrmArtifactDocumentsByBriefQuery, IReadOnlyList<CrmArtifactDocumentResponse>> getByBrief = sp.GetRequiredService<IQueryHandler<GetCrmArtifactDocumentsByBriefQuery, IReadOnlyList<CrmArtifactDocumentResponse>>>();
        IReadOnlyList<CrmArtifactDocumentResponse> artifacts = (await getByBrief.HandleAsync(new GetCrmArtifactDocumentsByBriefQuery(briefId, p.OwnerAppUserId))).Value!;

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
        // Office item/period/space messages are project-scoped (A-1); the owner acts with project-wide read.
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);

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
            new CreateCrmContactCommand(clientId, "Test", "Contact", ContactRole.Owner, IsPrimary: true, CrmTestAccess.Office(sp)));
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

    /// <summary>The scope's acting user, for messages that carry their caller's id.</summary>
    private static string Me(IServiceProvider sp) => sp.GetRequiredService<TestCurrentUser>().UserId!;
}
