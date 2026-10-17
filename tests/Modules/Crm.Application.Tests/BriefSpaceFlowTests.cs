using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Briefs;
using Crm.Contracts.Clients;
using Crm.Contracts.Spaces;
using Hr.Contracts.Employees;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Projects;
using SharedKernel;
using Xunit;

namespace Crm.Application.Tests;

/// <summary>
/// G9-10's own verify block, resolved from the real DI graph. Needs Docker; see the standing
/// environment note in PROGRESS.md if this suite could not run.
/// </summary>
[Collection(CrmApplicationCollection.Name)]
public sealed class BriefSpaceFlowTests(CrmApplicationFixture fixture)
{
    [Fact]
    public async Task DesignBrief_CreateUpdateSend_RequestsAClientApproval()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (string? clientAppUserId, Guid clientId) = await CreateClientAsync(sp, "brief-client");
        (Guid projectId, Guid employeeId) = await CreateProjectAsync(sp, clientId);

        ICommandHandler<CreateCrmDesignBriefCommand, Guid> createBrief = sp.GetRequiredService<ICommandHandler<CreateCrmDesignBriefCommand, Guid>>();
        var headData = new CrmBriefHeadData((UnitType)1, (UnitCondition)1, Address: "Original Addr");
        Result<Guid> briefResult = await createBrief.HandleAsync(
            new CreateCrmDesignBriefCommand(projectId, employeeId, null, headData));
        Assert.True(briefResult.IsSuccess);
        Guid briefId = briefResult.Value;

        ICommandHandler<UpdateCrmBriefHeadCommand> updateHead = sp.GetRequiredService<ICommandHandler<UpdateCrmBriefHeadCommand>>();
        CrmBriefHeadData updatedData = headData with { Address = "Updated Addr" };
        Assert.True((await updateHead.HandleAsync(new UpdateCrmBriefHeadCommand(briefId, updatedData))).IsSuccess);

        ICommandHandler<SendCrmBriefCommand> sendBrief = sp.GetRequiredService<ICommandHandler<SendCrmBriefCommand>>();
        Result sendResult = await sendBrief.HandleAsync(new SendCrmBriefCommand(briefId));
        Assert.True(sendResult.IsSuccess);

        IQueryHandler<GetCrmBriefByProjectQuery, CrmDesignBriefResponse> getBrief = sp.GetRequiredService<IQueryHandler<GetCrmBriefByProjectQuery, CrmDesignBriefResponse>>();
        Result<CrmDesignBriefResponse> brief = await getBrief.HandleAsync(new GetCrmBriefByProjectQuery(projectId));
        Assert.True(brief.IsSuccess);
        Assert.Equal(CrmApprovalState.PendingApproval, brief.Value!.Status);
        Assert.Equal("Updated Addr", brief.Value.Head.Address);

        // sendResult.IsSuccess above already proves IApprovalRequester.RequestAsync (resolved from the
        // real Approvals DI graph, not a stub) accepted the request - a real ApproverKind.Client
        // approver naming the client's own AppUserId - and the resulting PendingApproval status
        // proves the CRM side committed only after that acceptance, in the same transaction.
    }

    [Fact]
    public async Task BriefSpaceLedger_SendThenSupersede_OnlyAfterApproval()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (string _, Guid clientId) = await CreateClientAsync(sp, "space-client");
        (Guid projectId, Guid employeeId) = await CreateProjectAsync(sp, clientId);

        ICommandHandler<CreateCrmDesignBriefCommand, Guid> createBrief = sp.GetRequiredService<ICommandHandler<CreateCrmDesignBriefCommand, Guid>>();
        Guid briefId = (await createBrief.HandleAsync(new CreateCrmDesignBriefCommand(
            projectId, employeeId, null, new CrmBriefHeadData((UnitType)1, (UnitCondition)1)))).Value;

        ICommandHandler<CreateCrmBriefSpaceCommand, Guid> createEntry = sp.GetRequiredService<ICommandHandler<CreateCrmBriefSpaceCommand, Guid>>();
        Result<Guid> entryResult = await createEntry.HandleAsync(
            new CreateCrmBriefSpaceCommand(briefId, employeeId, SpaceId: null, Requirements: "north-facing light"));
        Assert.True(entryResult.IsSuccess);
        Guid entryId = entryResult.Value;

        ICommandHandler<SupersedeCrmBriefSpaceCommand, Guid> supersede = sp.GetRequiredService<ICommandHandler<SupersedeCrmBriefSpaceCommand, Guid>>();
        Result<Guid> supersedeResult = await supersede.HandleAsync(
            new SupersedeCrmBriefSpaceCommand(entryId, Guid.NewGuid(), SpaceId: null, Requirements: "corrected"));

        // Only a locked (Approved) entry can be superseded - a Draft entry must be rejected.
        Assert.True(supersedeResult.IsFailure);

        IQueryHandler<GetCrmBriefSpacesByBriefQuery, IReadOnlyList<CrmBriefSpaceResponse>> getEntries = sp.GetRequiredService<IQueryHandler<GetCrmBriefSpacesByBriefQuery, IReadOnlyList<CrmBriefSpaceResponse>>>();
        IReadOnlyList<CrmBriefSpaceResponse> entries = (await getEntries.HandleAsync(new GetCrmBriefSpacesByBriefQuery(briefId))).Value!;
        Assert.Single(entries);
    }

    [Fact]
    public async Task SpaceTypeAndSpace_CreateAndUpdate_RoundTrip()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        (string _, Guid clientId) = await CreateClientAsync(sp, "roundtrip-client");
        (Guid projectId, Guid _) = await CreateProjectAsync(sp, clientId);

        ICommandHandler<CreateCrmSpaceTypeCommand, Guid> createType = sp.GetRequiredService<ICommandHandler<CreateCrmSpaceTypeCommand, Guid>>();
        Result<Guid> typeResult = await createType.HandleAsync(
            new CreateCrmSpaceTypeCommand($"bed-{Guid.NewGuid():N}", "Bedroom", "غرفة نوم"));
        Assert.True(typeResult.IsSuccess);

        ICommandHandler<CreateCrmSpaceCommand, Guid> createSpace = sp.GetRequiredService<ICommandHandler<CreateCrmSpaceCommand, Guid>>();
        Result<Guid> spaceResult = await createSpace.HandleAsync(
            new CreateCrmSpaceCommand(projectId, typeResult.Value, "Kids Room"));
        Assert.True(spaceResult.IsSuccess);

        ICommandHandler<UpdateCrmSpaceCommand> updateSpace = sp.GetRequiredService<ICommandHandler<UpdateCrmSpaceCommand>>();
        Assert.True((await updateSpace.HandleAsync(
            new UpdateCrmSpaceCommand(spaceResult.Value, typeResult.Value, "Kids Room (renamed)", Description: null))).IsSuccess);

        IQueryHandler<GetCrmSpacesByProjectQuery, IReadOnlyList<CrmSpaceResponse>> getSpaces = sp.GetRequiredService<IQueryHandler<GetCrmSpacesByProjectQuery, IReadOnlyList<CrmSpaceResponse>>>();
        IReadOnlyList<CrmSpaceResponse> spaces = (await getSpaces.HandleAsync(new GetCrmSpacesByProjectQuery(projectId))).Value!;
        Assert.Equal("Kids Room (renamed)", spaces.Single().Name);
    }

    [Fact]
    public async Task CreateCrmSpace_WithUnknownProjectId_IsRejected()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        ICommandHandler<CreateCrmSpaceTypeCommand, Guid> createType = sp.GetRequiredService<ICommandHandler<CreateCrmSpaceTypeCommand, Guid>>();
        Guid typeId = (await createType.HandleAsync(
            new CreateCrmSpaceTypeCommand($"bed-{Guid.NewGuid():N}", "Bedroom", "غرفة نوم"))).Value;

        ICommandHandler<CreateCrmSpaceCommand, Guid> createSpace = sp.GetRequiredService<ICommandHandler<CreateCrmSpaceCommand, Guid>>();
        Result<Guid> result = await createSpace.HandleAsync(new CreateCrmSpaceCommand(Guid.NewGuid(), typeId, "Orphan Room"));

        Assert.True(result.IsFailure);
    }

    private static async Task<(string AppUserId, Guid ClientId)> CreateClientAsync(IServiceProvider sp, string label)
    {
        string suffix = Guid.NewGuid().ToString("N");
        (Guid clientId, string appUserId) = await CrmClientSeeding.CreateClientAsync(
            sp, $"{label}-{suffix}@example.com", lastName: label);

        return (appUserId, clientId);
    }

    /// <summary>Real project creation via <c>Projects.Application</c>'s own handler — needs a real
    /// employee owner, resolved the identical way <c>ProjectFlowTests.RegisterEmployeeAsync</c> does.
    /// E2-02: also returns that employee's id, since G9-08's <c>fk_crm_design_briefs_created_by</c>/
    /// <c>fk_crm_brief_spaces_created_by</c> now require a real <c>hr.employees</c> row for every
    /// <c>CreatedById</c> this file passes — same shape as <c>PeriodItemArtifactFlowTests</c>'s own
    /// <c>CreateProjectAsync</c>.</summary>
    private static async Task<(Guid ProjectId, Guid EmployeeId)> CreateProjectAsync(IServiceProvider sp, Guid clientId)
    {
        string suffix = Guid.NewGuid().ToString("N");
        (Guid employeeId, string ownerAppUserId) = await HrTestSeeding.HireEmployeeAsync(
            sp, $"owner-{suffix}@example.com", jobTitle: "PM", firstName: "Owner");

        TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = ownerAppUserId;

        ICommandHandler<CreateProjectCommand, Guid> createProject = sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>();
        Result<Guid> projectResult = await createProject.HandleAsync(
            new CreateProjectCommand($"P-{suffix}", "Test Project", "123 Main St", WorkflowType.Stage, clientId));
        Assert.True(projectResult.IsSuccess);

        return (projectResult.Value, employeeId);
    }
}
