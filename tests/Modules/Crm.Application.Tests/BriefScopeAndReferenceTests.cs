using Approvals.Contracts;
using Approvals.Contracts.Approvals;
using BuildingBlocks.Messaging;
using Crm.Application.Features.Briefs;
using Crm.Contracts.Briefs;
using Crm.Contracts.Spaces;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using SharedKernel;
using Xunit;
using ApprovalDecision = Approvals.Contracts.ApprovalDecision;
using CrmBriefHeadData = Crm.Contracts.Briefs.CrmBriefHeadData;
using UnitCondition = Crm.Contracts.UnitCondition;
using UnitType = Crm.Contracts.UnitType;

namespace Crm.Application.Tests;

/// <summary>
/// Run B of the CRM brief fixes: office brief messages are project-scoped (H5), every reference a brief
/// or entry carries belongs to its project (H2), a brief is one stage's (M1) and the ledger cannot fork
/// (M2). Every command runs in its own scope, as each HTTP request would.
/// </summary>
[Collection(CrmApplicationCollection.Name)]
public sealed class BriefScopeAndReferenceTests(CrmApplicationFixture fixture)
{
    private static readonly CrmBriefHeadData Head = new((UnitType)1, (UnitCondition)1);

    // ---- H5: project scope ----------------------------------------------------------------------

    [Fact]
    public async Task EmployeeOutsideTheProject_Gets404_OnEveryBriefMessage()
    {
        BriefTestProject p = await SeedAsync("scope-outsider");
        Guid briefId = await CreateBriefAsync(p, p.StageId);
        Guid entryId = await CreateEntryAsync(p, briefId);
        string outsider = await HireAsync("outsider");

        Result[] results =
        [
            Plain(await CommandFor<CreateCrmDesignBriefCommand, Guid>(outsider, new CreateCrmDesignBriefCommand(p.ProjectId, p.EmployeeId, p.StageId, Head, outsider))),
            await Command(outsider, new UpdateCrmBriefHeadCommand(briefId, Head, outsider)),
            await Command(outsider, new SendCrmBriefCommand(briefId, outsider)),
            Plain(await CommandFor<CreateCrmBriefSpaceCommand, Guid>(outsider, new CreateCrmBriefSpaceCommand(briefId, p.EmployeeId, outsider, SpaceId: null))),
            await Command(outsider, new UpdateCrmBriefSpaceCommand(entryId, outsider, null, 1, "x", null, null)),
            await Command(outsider, new SendCrmBriefSpaceCommand(entryId, outsider)),
            Plain(await CommandFor<SupersedeCrmBriefSpaceCommand, Guid>(outsider, new SupersedeCrmBriefSpaceCommand(entryId, p.EmployeeId, outsider, SpaceId: null))),
            Plain(await QueryFor<GetCrmBriefByProjectQuery, CrmDesignBriefResponse>(outsider, new GetCrmBriefByProjectQuery(p.ProjectId, outsider))),
            Plain(await QueryFor<GetCrmBriefByStageQuery, CrmDesignBriefResponse>(outsider, new GetCrmBriefByStageQuery(p.StageId, outsider))),
            Plain(await QueryFor<GetCrmBriefSpacesByBriefQuery, IReadOnlyList<CrmBriefSpaceResponse>>(outsider, new GetCrmBriefSpacesByBriefQuery(briefId, outsider))),
        ];

        Assert.All(results, r =>
        {
            Assert.True(r.IsFailure);
            Assert.Equal(CrmBriefErrors.NotFound, r.Error);
            Assert.Equal(404, r.Error.Status);
        });
        Assert.Equal(Domain.ApprovalState.Draft, (await LoadBriefAsync(briefId)).Status);
    }

    [Fact]
    public async Task StageTeamMember_WithoutReadAll_CanWorkTheBrief_ButGets404OnAnotherProject()
    {
        (Guid headEmployeeId, string _) = await HireEmployeeAsync("dept-head");
        BriefTestProject mine = await SeedAsync("scope-member", departmentHeadEmployeeId: headEmployeeId);
        BriefTestProject other = await SeedAsync("scope-other");
        (Guid memberEmployeeId, string member) = await HireEmployeeAsync("member");
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            await BriefTestSeeding.AddStageTeamMemberAsync(
                scope.ServiceProvider, fixture.ConnectionString, mine, mine.StageId, memberEmployeeId);
        }

        Guid otherBriefId = await CreateBriefAsync(other, other.StageId);
        Guid otherEntryId = await CreateEntryAsync(other, otherBriefId);

        // On their own project the member (no read_all) gets through the scope.
        Result<Guid> created = await CommandFor<CreateCrmDesignBriefCommand, Guid>(
            member, new CreateCrmDesignBriefCommand(mine.ProjectId, memberEmployeeId, mine.StageId, Head, member));
        Assert.True(created.IsSuccess, created.Error?.Description);
        Assert.True((await QueryFor<GetCrmBriefByStageQuery, CrmDesignBriefResponse>(member, new GetCrmBriefByStageQuery(mine.StageId, member))).IsSuccess);
        Assert.True((await Command(member, new UpdateCrmBriefHeadCommand(created.Value, Head, member))).IsSuccess);

        // Another project's brief, entries and stage are not theirs: 404, never 403.
        Result[] crossProject =
        [
            Plain(await QueryFor<GetCrmBriefByProjectQuery, CrmDesignBriefResponse>(member, new GetCrmBriefByProjectQuery(other.ProjectId, member))),
            Plain(await QueryFor<GetCrmBriefByStageQuery, CrmDesignBriefResponse>(member, new GetCrmBriefByStageQuery(other.StageId, member))),
            Plain(await QueryFor<GetCrmBriefSpacesByBriefQuery, IReadOnlyList<CrmBriefSpaceResponse>>(member, new GetCrmBriefSpacesByBriefQuery(otherBriefId, member))),
            await Command(member, new UpdateCrmBriefHeadCommand(otherBriefId, Head, member)),
            await Command(member, new UpdateCrmBriefSpaceCommand(otherEntryId, member, null, 1, "x", null, null)),
        ];
        Assert.All(crossProject, r => Assert.Equal(404, r.Error.Status));
    }

    // ---- H2: references -------------------------------------------------------------------------

    [Fact]
    public async Task CreateBrief_WithAnotherProjectsStage_Is400()
    {
        BriefTestProject p = await SeedAsync("ref-stage");
        BriefTestProject other = await SeedAsync("ref-stage-other");

        Result<Guid> result = await CommandFor<CreateCrmDesignBriefCommand, Guid>(
            p.OwnerAppUserId, new CreateCrmDesignBriefCommand(p.ProjectId, p.EmployeeId, other.StageId, Head, p.OwnerAppUserId));

        Assert.Equal(CrmBriefErrors.StageProjectMismatch, result.Error);
        Assert.Equal(400, result.Error.Status);
    }

    [Fact]
    public async Task Entry_WithForeignSpaceTaskOrMeeting_Is400_OnCreateUpdateAndSupersede()
    {
        BriefTestProject p = await SeedAsync("ref-entry", stageCount: 2);
        BriefTestProject other = await SeedAsync("ref-entry-other");
        Guid briefId = await CreateBriefAsync(p, p.StageId);
        Guid entryId = await CreateEntryAsync(p, briefId);
        Guid foreignSpace = await CreateSpaceAsync(other);
        Guid foreignMeeting = await CreateMeetingAsync(other);
        Guid otherStagesTask = p.TaskIds[1];
        string u = p.OwnerAppUserId;

        Assert.Equal(CrmBriefErrors.SpaceProjectMismatch, (await CommandFor<CreateCrmBriefSpaceCommand, Guid>(
            u, new CreateCrmBriefSpaceCommand(briefId, p.EmployeeId, u, SpaceId: foreignSpace))).Error);
        Assert.Equal(CrmBriefErrors.TaskProjectMismatch, (await CommandFor<CreateCrmBriefSpaceCommand, Guid>(
            u, new CreateCrmBriefSpaceCommand(briefId, p.EmployeeId, u, SpaceId: null, ProjectTaskId: other.TaskId))).Error);
        Assert.Equal(CrmBriefErrors.TaskStageMismatch, (await CommandFor<CreateCrmBriefSpaceCommand, Guid>(
            u, new CreateCrmBriefSpaceCommand(briefId, p.EmployeeId, u, SpaceId: null, ProjectTaskId: otherStagesTask))).Error);
        Assert.Equal(CrmBriefErrors.MeetingProjectMismatch, (await CommandFor<CreateCrmBriefSpaceCommand, Guid>(
            u, new CreateCrmBriefSpaceCommand(briefId, p.EmployeeId, u, SpaceId: null, MeetingId: foreignMeeting))).Error);

        Assert.Equal(CrmBriefErrors.SpaceProjectMismatch, (await Command(
            u, new UpdateCrmBriefSpaceCommand(entryId, u, foreignSpace, 1, "x", null, null))).Error);
        Assert.Equal(CrmBriefErrors.TaskProjectMismatch, (await Command(
            u, new UpdateCrmBriefSpaceCommand(entryId, u, null, 1, "x", null, other.TaskId))).Error);
        Assert.Equal(CrmBriefErrors.MeetingProjectMismatch, (await Command(
            u, new UpdateCrmBriefSpaceCommand(entryId, u, null, 1, "x", foreignMeeting, null))).Error);

        await ApproveEntryAsync(p, entryId);
        Assert.Equal(CrmBriefErrors.MeetingProjectMismatch, (await CommandFor<SupersedeCrmBriefSpaceCommand, Guid>(
            u, new SupersedeCrmBriefSpaceCommand(entryId, p.EmployeeId, u, SpaceId: null, MeetingId: foreignMeeting))).Error);
        Assert.Equal(CrmBriefErrors.TaskProjectMismatch, (await CommandFor<SupersedeCrmBriefSpaceCommand, Guid>(
            u, new SupersedeCrmBriefSpaceCommand(entryId, p.EmployeeId, u, SpaceId: null, ProjectTaskId: other.TaskId))).Error);

        // The brief's own space, own stage's task and own meeting are accepted.
        Guid ownSpace = await CreateSpaceAsync(p);
        Guid ownMeeting = await CreateMeetingAsync(p);
        Result<Guid> ok = await CommandFor<CreateCrmBriefSpaceCommand, Guid>(u, new CreateCrmBriefSpaceCommand(
            briefId, p.EmployeeId, u, SpaceId: ownSpace, MeetingId: ownMeeting, ProjectTaskId: p.TaskId));
        Assert.True(ok.IsSuccess, ok.Error?.Description);
    }

    // ---- M2: the ledger cannot fork -------------------------------------------------------------

    [Fact]
    public async Task SupersedingTheSameEntryTwice_Is409_AndACorrectionOnAnotherSpaceIs400()
    {
        BriefTestProject p = await SeedAsync("ledger-fork");
        Guid briefId = await CreateBriefAsync(p, p.StageId);
        Guid entryId = await CreateEntryAsync(p, briefId);
        await ApproveEntryAsync(p, entryId);
        string u = p.OwnerAppUserId;

        Result<Guid> movedSpace = await CommandFor<SupersedeCrmBriefSpaceCommand, Guid>(
            u, new SupersedeCrmBriefSpaceCommand(entryId, p.EmployeeId, u, SpaceId: await CreateSpaceAsync(p)));
        Assert.Equal(CrmBriefErrors.SupersedeSpaceMismatch, movedSpace.Error);

        Result<Guid> first = await CommandFor<SupersedeCrmBriefSpaceCommand, Guid>(
            u, new SupersedeCrmBriefSpaceCommand(entryId, p.EmployeeId, u, SpaceId: null, Requirements: "first"));
        Result<Guid> second = await CommandFor<SupersedeCrmBriefSpaceCommand, Guid>(
            u, new SupersedeCrmBriefSpaceCommand(entryId, p.EmployeeId, u, SpaceId: null, Requirements: "second"));

        Assert.True(first.IsSuccess);
        Assert.Equal(CrmBriefErrors.AlreadySuperseded, second.Error);
        Assert.Equal(409, second.Error.Status);
    }

    /// <summary>Both pass the pre-check; the partial unique index lets one through and the other's
    /// violation maps to the same 409.</summary>
    [Fact]
    public async Task ConcurrentSupersedes_ExactlyOneWins()
    {
        BriefTestProject p = await SeedAsync("ledger-race");
        Guid briefId = await CreateBriefAsync(p, p.StageId);
        Guid entryId = await CreateEntryAsync(p, briefId);
        await ApproveEntryAsync(p, entryId);
        string u = p.OwnerAppUserId;

        Result<Guid>[] results = await Task.WhenAll(
            CommandFor<SupersedeCrmBriefSpaceCommand, Guid>(u, new SupersedeCrmBriefSpaceCommand(entryId, p.EmployeeId, u, SpaceId: null)),
            CommandFor<SupersedeCrmBriefSpaceCommand, Guid>(u, new SupersedeCrmBriefSpaceCommand(entryId, p.EmployeeId, u, SpaceId: null)));

        Assert.Single(results, r => r.IsSuccess);
        Assert.Equal(CrmBriefErrors.AlreadySuperseded, Assert.Single(results, r => r.IsFailure).Error);
    }

    // ---- M1: one brief per stage ----------------------------------------------------------------

    [Fact]
    public async Task StageRoute_ReturnsThatStagesBrief_AndProjectRoute_ReturnsTheLatest()
    {
        BriefTestProject p = await SeedAsync("one-per-stage", stageCount: 2);
        Guid firstStageBrief = await CreateBriefAsync(p, p.StageIds[0]);
        Guid secondStageBrief = await CreateBriefAsync(p, p.StageIds[1]);
        string u = p.OwnerAppUserId;

        Result<CrmDesignBriefResponse> byStage = await QueryFor<GetCrmBriefByStageQuery, CrmDesignBriefResponse>(
            u, new GetCrmBriefByStageQuery(p.StageIds[0], u));
        Result<CrmDesignBriefResponse> byProject = await QueryFor<GetCrmBriefByProjectQuery, CrmDesignBriefResponse>(
            u, new GetCrmBriefByProjectQuery(p.ProjectId, u));

        Assert.Equal(firstStageBrief, byStage.Value!.Id);
        Assert.Equal(p.StageIds[0], byStage.Value.ProjectStageId);
        Assert.Equal(secondStageBrief, byProject.Value!.Id);
    }

    // ---- helpers --------------------------------------------------------------------------------

    private readonly HashSet<string> _owners = [];

    private bool IsOwner(string userId) => _owners.Contains(userId);

    private async Task<BriefTestProject> SeedAsync(string label, int stageCount = 1, Guid? departmentHeadEmployeeId = null)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        BriefTestProject p = await BriefTestSeeding.CreateProjectWithStageAsync(
            scope.ServiceProvider, label, stageCount, departmentHeadEmployeeId);
        _owners.Add(p.OwnerAppUserId);
        return p;
    }

    private async Task<(Guid EmployeeId, string AppUserId)> HireEmployeeAsync(string label)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await HrTestSeeding.HireEmployeeAsync(
            scope.ServiceProvider, $"{label}-{Guid.NewGuid():N}@example.com", jobTitle: "Designer", firstName: label);
    }

    private async Task<string> HireAsync(string label) => (await HireEmployeeAsync(label)).AppUserId;

    /// <summary>A fresh scope acting as <paramref name="userId"/>. Project owners here are not on any
    /// stage team, so they act with <c>project.read_all</c>; everyone else (outsiders, team members)
    /// acts with no permission at all, so their scope decision rests on stage-team membership alone.</summary>
    private AsyncServiceScope ScopeFor(string userId, bool readAll)
    {
        AsyncServiceScope scope = fixture.CreateScope();
        TestCurrentUser currentUser = scope.ServiceProvider.GetRequiredService<TestCurrentUser>();
        currentUser.UserId = userId;
        if (readAll)
        {
            currentUser.Grant(ProjectsPermissions.Projects.ReadAll);
        }

        return scope;
    }

    private async Task<Result> Command<TCommand>(string userId, TCommand command) where TCommand : ICommand
    {
        await using AsyncServiceScope scope = ScopeFor(userId, IsOwner(userId));
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(command);
    }

    private async Task<Result<TResponse>> CommandFor<TCommand, TResponse>(string userId, TCommand command)
        where TCommand : ICommand<TResponse>
    {
        await using AsyncServiceScope scope = ScopeFor(userId, IsOwner(userId));
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>().HandleAsync(command);
    }

    private async Task<Result<TResponse>> QueryFor<TQuery, TResponse>(string userId, TQuery query)
        where TQuery : IQuery<TResponse>
    {
        await using AsyncServiceScope scope = ScopeFor(userId, IsOwner(userId));
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<TQuery, TResponse>>().HandleAsync(query);
    }

    private static Result Plain<T>(Result<T> result) => result.IsSuccess ? Result.Success() : result.Error;

    private async Task<Guid> CreateBriefAsync(BriefTestProject p, Guid stageId)
    {
        Result<Guid> result = await CommandFor<CreateCrmDesignBriefCommand, Guid>(
            p.OwnerAppUserId, new CreateCrmDesignBriefCommand(p.ProjectId, p.EmployeeId, stageId, Head, p.OwnerAppUserId));
        Assert.True(result.IsSuccess, result.Error?.Description);
        return result.Value;
    }

    private async Task<Guid> CreateEntryAsync(BriefTestProject p, Guid briefId)
    {
        Result<Guid> result = await CommandFor<CreateCrmBriefSpaceCommand, Guid>(
            p.OwnerAppUserId, new CreateCrmBriefSpaceCommand(briefId, p.EmployeeId, p.OwnerAppUserId, SpaceId: null, Requirements: "light"));
        Assert.True(result.IsSuccess, result.Error?.Description);
        return result.Value;
    }

    private async Task<Guid> CreateSpaceAsync(BriefTestProject p)
    {
        await using AsyncServiceScope scope = ScopeFor(p.OwnerAppUserId, readAll: true);
        IServiceProvider sp = scope.ServiceProvider;
        Guid typeId = (await sp.GetRequiredService<ICommandHandler<CreateCrmSpaceTypeCommand, Guid>>()
            .HandleAsync(new CreateCrmSpaceTypeCommand($"room-{Guid.NewGuid():N}", "Room", "غرفة"))).Value;
        Result<Guid> space = await sp.GetRequiredService<ICommandHandler<CreateCrmSpaceCommand, Guid>>()
            .HandleAsync(new CreateCrmSpaceCommand(p.ProjectId, typeId, "Room", p.OwnerAppUserId));
        Assert.True(space.IsSuccess, space.Error?.Description);
        return space.Value;
    }

    private async Task<Guid> CreateMeetingAsync(BriefTestProject p)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        CrmDbContext db = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        CrmMeeting meeting = CrmMeeting.Create(p.ProjectId, null, "Kickoff", MeetingType.InOffice, p.EmployeeId, null).Value!;
        db.CrmMeetings.Add(meeting);
        await db.SaveChangesAsync();
        return meeting.Id;
    }

    /// <summary>Send, then the client decides through the handler (not the portal route — M6).</summary>
    private async Task ApproveEntryAsync(BriefTestProject p, Guid entryId)
    {
        Assert.True((await Command(p.OwnerAppUserId, new SendCrmBriefSpaceCommand(entryId, p.OwnerAppUserId))).IsSuccess);

        await using (AsyncServiceScope scope = ScopeFor(p.ClientAppUserId, readAll: false))
        {
            Result decided = await scope.ServiceProvider.GetRequiredService<ICommandHandler<DecideApprovalCommand>>()
                .HandleAsync(new DecideApprovalCommand(
                    CrmApprovalIds.For(ApprovalSubjectType.BriefSpace, entryId, 1), ApprovalDecision.Approved, Comment: null));
            Assert.True(decided.IsSuccess, decided.Error?.Description);
        }

        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
    }

    private async Task<CrmDesignBrief> LoadBriefAsync(Guid briefId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<CrmDbContext>()
            .CrmDesignBriefs.AsNoTracking().SingleAsync(b => b.Id == briefId);
    }
}
