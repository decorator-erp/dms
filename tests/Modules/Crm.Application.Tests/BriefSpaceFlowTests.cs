using BuildingBlocks.Messaging;
using Crm.Contracts;
using Crm.Contracts.Briefs;
using Crm.Contracts.Clients;
using Crm.Contracts.Spaces;
using Hr.Contracts.Employees;
using Identity.Contracts.Auth;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Projects;
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
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (clientAppUserId, clientId) = await CreateClientAsync(sp, "brief-client");
        var projectId = await CreateProjectAsync(sp, clientId);

        var createBrief = sp.GetRequiredService<ICommandHandler<CreateCrmDesignBriefCommand, Guid>>();
        var headData = new CrmBriefHeadData((UnitType)1, (UnitCondition)1, Address: "Original Addr");
        var briefResult = await createBrief.HandleAsync(
            new CreateCrmDesignBriefCommand(projectId, Guid.NewGuid(), null, headData));
        Assert.True(briefResult.IsSuccess);
        var briefId = briefResult.Value;

        var updateHead = sp.GetRequiredService<ICommandHandler<UpdateCrmBriefHeadCommand>>();
        var updatedData = headData with { Address = "Updated Addr" };
        Assert.True((await updateHead.HandleAsync(new UpdateCrmBriefHeadCommand(briefId, updatedData))).IsSuccess);

        var sendBrief = sp.GetRequiredService<ICommandHandler<SendCrmBriefCommand>>();
        var sendResult = await sendBrief.HandleAsync(new SendCrmBriefCommand(briefId));
        Assert.True(sendResult.IsSuccess);

        var getBrief = sp.GetRequiredService<IQueryHandler<GetCrmBriefByProjectQuery, CrmDesignBriefResponse>>();
        var brief = await getBrief.HandleAsync(new GetCrmBriefByProjectQuery(projectId));
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
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (_, clientId) = await CreateClientAsync(sp, "space-client");
        var projectId = await CreateProjectAsync(sp, clientId);

        var createBrief = sp.GetRequiredService<ICommandHandler<CreateCrmDesignBriefCommand, Guid>>();
        var briefId = (await createBrief.HandleAsync(new CreateCrmDesignBriefCommand(
            projectId, Guid.NewGuid(), null, new CrmBriefHeadData((UnitType)1, (UnitCondition)1)))).Value;

        var createEntry = sp.GetRequiredService<ICommandHandler<CreateCrmBriefSpaceCommand, Guid>>();
        var entryResult = await createEntry.HandleAsync(
            new CreateCrmBriefSpaceCommand(briefId, Guid.NewGuid(), SpaceId: null, Requirements: "north-facing light"));
        Assert.True(entryResult.IsSuccess);
        var entryId = entryResult.Value;

        var supersede = sp.GetRequiredService<ICommandHandler<SupersedeCrmBriefSpaceCommand, Guid>>();
        var supersedeResult = await supersede.HandleAsync(
            new SupersedeCrmBriefSpaceCommand(entryId, Guid.NewGuid(), SpaceId: null, Requirements: "corrected"));

        // Only a locked (Approved) entry can be superseded - a Draft entry must be rejected.
        Assert.True(supersedeResult.IsFailure);

        var getEntries = sp.GetRequiredService<IQueryHandler<GetCrmBriefSpacesByBriefQuery, IReadOnlyList<CrmBriefSpaceResponse>>>();
        var entries = (await getEntries.HandleAsync(new GetCrmBriefSpacesByBriefQuery(briefId))).Value!;
        Assert.Single(entries);
    }

    [Fact]
    public async Task SpaceTypeAndSpace_CreateAndUpdate_RoundTrip()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (_, clientId) = await CreateClientAsync(sp, "roundtrip-client");
        var projectId = await CreateProjectAsync(sp, clientId);

        var createType = sp.GetRequiredService<ICommandHandler<CreateCrmSpaceTypeCommand, Guid>>();
        var typeResult = await createType.HandleAsync(
            new CreateCrmSpaceTypeCommand($"bed-{Guid.NewGuid():N}", "Bedroom", "غرفة نوم"));
        Assert.True(typeResult.IsSuccess);

        var createSpace = sp.GetRequiredService<ICommandHandler<CreateCrmSpaceCommand, Guid>>();
        var spaceResult = await createSpace.HandleAsync(
            new CreateCrmSpaceCommand(projectId, typeResult.Value, "Kids Room"));
        Assert.True(spaceResult.IsSuccess);

        var updateSpace = sp.GetRequiredService<ICommandHandler<UpdateCrmSpaceCommand>>();
        Assert.True((await updateSpace.HandleAsync(
            new UpdateCrmSpaceCommand(spaceResult.Value, typeResult.Value, "Kids Room (renamed)", Description: null))).IsSuccess);

        var getSpaces = sp.GetRequiredService<IQueryHandler<GetCrmSpacesByProjectQuery, IReadOnlyList<CrmSpaceResponse>>>();
        var spaces = (await getSpaces.HandleAsync(new GetCrmSpacesByProjectQuery(projectId))).Value!;
        Assert.Equal("Kids Room (renamed)", spaces.Single().Name);
    }

    [Fact]
    public async Task CreateCrmSpace_WithUnknownProjectId_IsRejected()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var createType = sp.GetRequiredService<ICommandHandler<CreateCrmSpaceTypeCommand, Guid>>();
        var typeId = (await createType.HandleAsync(
            new CreateCrmSpaceTypeCommand($"bed-{Guid.NewGuid():N}", "Bedroom", "غرفة نوم"))).Value;

        var createSpace = sp.GetRequiredService<ICommandHandler<CreateCrmSpaceCommand, Guid>>();
        var result = await createSpace.HandleAsync(new CreateCrmSpaceCommand(Guid.NewGuid(), typeId, "Orphan Room"));

        Assert.True(result.IsFailure);
    }

    private static async Task<(string AppUserId, Guid ClientId)> CreateClientAsync(IServiceProvider sp, string label)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var register = sp.GetRequiredService<ICommandHandler<RegisterCommand, string>>();
        var registerResult = await register.HandleAsync(new RegisterCommand(
            new RegisterRequest($"{label}-{suffix}@example.com", "P@ssw0rd123!", "Test", label, null, "employee")));
        Assert.True(registerResult.IsSuccess);
        var appUserId = registerResult.Value!;

        var createClient = sp.GetRequiredService<ICommandHandler<CreateClientCommand, Guid>>();
        var clientResult = await createClient.HandleAsync(new CreateClientCommand(appUserId, "1 Test Way"));
        Assert.True(clientResult.IsSuccess);

        return (appUserId, clientResult.Value);
    }

    /// <summary>Real project creation via <c>Projects.Application</c>'s own handler — needs a real
    /// employee owner, resolved the identical way <c>ProjectFlowTests.RegisterEmployeeAsync</c>
    /// does.</summary>
    private static async Task<Guid> CreateProjectAsync(IServiceProvider sp, Guid clientId)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var register = sp.GetRequiredService<ICommandHandler<RegisterCommand, string>>();
        var ownerAppUserId = (await register.HandleAsync(new RegisterCommand(
            new RegisterRequest($"owner-{suffix}@example.com", "P@ssw0rd123!", "Owner", "Test", null, "employee")))).Value!;

        var createEmployee = sp.GetRequiredService<ICommandHandler<CreateEmployeeCommand, Guid>>();
        Assert.True((await createEmployee.HandleAsync(
            new CreateEmployeeCommand(ownerAppUserId, "PM", DateOnly.FromDateTime(DateTime.UtcNow), "FullTime"))).IsSuccess);

        var currentUser = sp.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = ownerAppUserId;

        var createProject = sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>();
        var projectResult = await createProject.HandleAsync(
            new CreateProjectCommand($"P-{suffix}", "Test Project", "123 Main St", WorkflowType.Stage, clientId));
        Assert.True(projectResult.IsSuccess);

        return projectResult.Value;
    }
}
