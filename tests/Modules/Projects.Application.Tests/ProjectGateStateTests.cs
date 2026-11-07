using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Gates;
using Projects.Contracts.Stages;
using Projects.Contracts.Tasks;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Xunit;

namespace Projects.Application.Tests;

/// <summary>Sites/Projects review, phase 4 (Projects gates and state rules): one red test per finding.</summary>
[Collection(ProjectsApplicationCollection.Name)]
public sealed class ProjectGateStateTests(ProjectsApplicationFixture fixture)
{
    /// <summary>P3: only a Pending ManualCheck gate is satisfied by hand — a pending approval gate, or a manual gate
    /// already satisfied, is 409 <c>gate_conditions.NotManuallySatisfiable</c>; a note over 2000 characters is a 400.</summary>
    [Fact]
    public async Task P3_OnlyAPendingManualCheckGate_IsSatisfiedByHand()
    {
        (ScopeTestProject p, _, Guid[] tasks) = await StageProjectWithTasksAsync("p3", 1);

        Guid approval = await CreateTaskGateAsync(p, tasks[0], ConditionType.Approval, GateTrigger.Complete,
            approvalType: ApprovalType.ManagerApproval);
        Result forced = await RunAsync(p, new SatisfyGateConditionCommand(p.ProjectId, approval, p.OfficeUserId));
        AssertError(forced, "gate_conditions.NotManuallySatisfiable", 409);
        Assert.Equal(Domain.GateStatus.Pending, await GateStatusAsync(approval));

        Guid manual = await CreateTaskGateAsync(p, tasks[0], ConditionType.ManualCheck, GateTrigger.Complete);
        Result tooLong = await RunAsync(p, new SatisfyGateConditionCommand(p.ProjectId, manual, p.OfficeUserId, new string('n', 2001)));
        Assert.True(tooLong.IsFailure);
        Assert.Equal(400, tooLong.Error.Status);

        await ExpectOkAsync(p, new SatisfyGateConditionCommand(p.ProjectId, manual, p.OfficeUserId, "Checked on site"));
        Result again = await RunAsync(p, new SatisfyGateConditionCommand(p.ProjectId, manual, p.OfficeUserId));
        AssertError(again, "gate_conditions.NotManuallySatisfiable", 409);
    }


    /// <summary>NP1: with two Unlock gates on one task, satisfying the first leaves the task Locked; it opens only when
    /// the last pending Unlock gate is satisfied.</summary>
    [Fact]
    public async Task NP1_ATaskWithTwoUnlockGates_OpensOnlyWhenTheLastIsSatisfied()
    {
        (ScopeTestProject p, _, Guid[] tasks) = await StageProjectWithTasksAsync("np1", 1);
        Guid first = await CreateTaskGateAsync(p, tasks[0], ConditionType.ManualCheck, GateTrigger.Unlock);
        Guid second = await CreateTaskGateAsync(p, tasks[0], ConditionType.ManualCheck, GateTrigger.Unlock);
        Assert.Equal(Domain.ProjectTaskStatus.Locked, await TaskStatusAsync(tasks[0]));

        await ExpectOkAsync(p, new SatisfyGateConditionCommand(p.ProjectId, first, p.OfficeUserId));
        Assert.Equal(Domain.ProjectTaskStatus.Locked, await TaskStatusAsync(tasks[0]));

        await ExpectOkAsync(p, new SatisfyGateConditionCommand(p.ProjectId, second, p.OfficeUserId));
        Assert.Equal(Domain.ProjectTaskStatus.Open, await TaskStatusAsync(tasks[0]));
    }

    /// <summary>P4: a satisfied gate and a pending approval gate are 409 <c>gate_conditions.NotDeletable</c>; a task gate
    /// needs <c>stage_tasks.manage_gates</c> (403 with only the stage claim); deleting the owner's last pending Unlock
    /// gate releases the owner.</summary>
    [Fact]
    public async Task P4_GateDelete_RefusesSatisfiedAndPendingApprovalGates_AndReleasesTheOwner()
    {
        (ScopeTestProject p, Guid stageId, Guid[] tasks) = await StageProjectWithTasksAsync("p4", 1);

        Guid approval = await CreateTaskGateAsync(p, tasks[0], ConditionType.Approval, GateTrigger.Complete,
            approvalType: ApprovalType.ManagerApproval);
        AssertError(await RunAsync(p, new DeleteGateConditionCommand(p.ProjectId, approval, p.OfficeUserId)),
            "gate_conditions.NotDeletable", 409);

        Guid satisfied = await CreateStageGateAsync(p, stageId, ConditionType.ManualCheck, GateTrigger.Complete);
        await ExpectOkAsync(p, new SatisfyGateConditionCommand(p.ProjectId, satisfied, p.OfficeUserId));
        AssertError(await RunAsync(p, new DeleteGateConditionCommand(p.ProjectId, satisfied, p.OfficeUserId)),
            "gate_conditions.NotDeletable", 409);

        Guid unlock = await CreateTaskGateAsync(p, tasks[0], ConditionType.ManualCheck, GateTrigger.Unlock);
        Assert.Equal(Domain.ProjectTaskStatus.Locked, await TaskStatusAsync(tasks[0]));
        AssertError(await RunAsync(p, new DeleteGateConditionCommand(p.ProjectId, unlock, p.OfficeUserId),
            ProjectsPermissions.Stages.ManageGates), "projects.AccessDenied", 403);

        await ExpectOkAsync(p, new DeleteGateConditionCommand(p.ProjectId, unlock, p.OfficeUserId));
        Assert.Equal(Domain.ProjectTaskStatus.Open, await TaskStatusAsync(tasks[0]));
    }

    /// <summary>B-P-H4: a rejected Unlock gate — an approval rejected through Approvals, or a Finance variation
    /// cancelled — releases its owner once no other Unlock gate of it is pending, instead of leaving it Locked.</summary>
    [Fact]
    public async Task BPH4_ARejectedUnlockGate_ReleasesItsOwner()
    {
        (ScopeTestProject p, Guid stageId, Guid[] tasks) = await StageProjectWithTasksAsync("bph4", 1);

        Guid approval = await CreateTaskGateAsync(p, tasks[0], ConditionType.Approval, GateTrigger.Unlock,
            approvalType: ApprovalType.ManagerApproval);
        Assert.Equal(Domain.ProjectTaskStatus.Locked, await TaskStatusAsync(tasks[0]));
        Guid approvalId = await ApprovalIdAsync(approval);
        await using (AsyncServiceScope scope = GateScope(p))
        {
            Result decided = await scope.ServiceProvider.GetRequiredService<ICommandHandler<Approvals.Contracts.Approvals.DecideApprovalCommand>>()
                .HandleAsync(new Approvals.Contracts.Approvals.DecideApprovalCommand(
                    approvalId, Approvals.Contracts.ApprovalDecision.Rejected, "No"));
            Assert.True(decided.IsSuccess, decided.IsFailure ? decided.Error.ToString() : null);
        }

        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);
        Assert.Equal(Domain.GateStatus.Rejected, await GateStatusAsync(approval));
        Assert.Equal(Domain.ProjectTaskStatus.Open, await TaskStatusAsync(tasks[0]));

        var variationId = Guid.NewGuid();
        await PublishAsync(new Finance.Contracts.VariationRaisedIntegrationEvent(
            Guid.NewGuid(), variationId, p.ProjectId, stageId, null, DateTimeOffset.UtcNow));
        Assert.Equal(Domain.StageStatus.Locked, await StageStatusAsync(stageId));
        await PublishAsync(new Finance.Contracts.VariationClosedIntegrationEvent(
            Guid.NewGuid(), variationId, p.ProjectId, Cancelled: true, DateTimeOffset.UtcNow));
        Assert.Equal(Domain.StageStatus.Active, await StageStatusAsync(stageId));
    }

    /// <summary>NP2: an Unlock gate is created only on an owner that is open (a task Open, a stage Active) or already
    /// Locked; on a started, completed or rejected owner it is 409 <c>gate_conditions.OwnerNotOpen</c> and the owner
    /// keeps its state. A Finance variation raised on such an owner opens no gate (skipped and logged).</summary>
    [Fact]
    public async Task NP2_AnUnlockGateOnAStartedOrClosedOwner_IsRefused()
    {
        (ScopeTestProject p, Guid stageId, Guid[] tasks) = await StageProjectWithTasksAsync("np2", 2);
        await ExpectOkAsync(p, new StartProjectTaskCommand(p.ProjectId, tasks[0], p.OfficeUserId));

        Result<Guid> onStarted = await RunAsync<CreateTaskGateConditionCommand, Guid>(p, new CreateTaskGateConditionCommand(
            p.ProjectId, tasks[0], p.OfficeUserId, ConditionType.ManualCheck, GateTrigger.Unlock, "Too late"));
        AssertError(onStarted, "gate_conditions.OwnerNotOpen", 409);
        Assert.Equal(Domain.ProjectTaskStatus.InProgress, await TaskStatusAsync(tasks[0]));

        // A Complete gate on a started task is still fine, and a second Unlock gate on a Locked task too.
        await CreateTaskGateAsync(p, tasks[0], ConditionType.ManualCheck, GateTrigger.Complete);
        await CreateTaskGateAsync(p, tasks[1], ConditionType.ManualCheck, GateTrigger.Unlock);
        await CreateTaskGateAsync(p, tasks[1], ConditionType.ManualCheck, GateTrigger.Unlock);

        await PublishAsync(new Finance.Contracts.VariationRaisedIntegrationEvent(
            Guid.NewGuid(), Guid.NewGuid(), p.ProjectId, null, tasks[0], DateTimeOffset.UtcNow));
        Assert.Equal(Domain.ProjectTaskStatus.InProgress, await TaskStatusAsync(tasks[0]));

        await ExpectOkAsync(p, new RejectProjectStageCommand(p.ProjectId, stageId, p.OfficeUserId, "Redo"));
        Result<Guid> onRejected = await RunAsync<CreateStageGateConditionCommand, Guid>(p, new CreateStageGateConditionCommand(
            p.ProjectId, stageId, p.OfficeUserId, ConditionType.ManualCheck, GateTrigger.Unlock, "Too late"));
        AssertError(onRejected, "gate_conditions.OwnerNotOpen", 409);
        Assert.Equal(Domain.StageStatus.Rejected, await StageStatusAsync(stageId));
    }

    /// <summary>B-P-M7: an approval gate's approval is requested under an id derived from the gate id, so the two stay
    /// bound even when the gate's own save fails after Approvals committed.</summary>
    [Fact]
    public async Task BPM7_AnApprovalGate_IsRequestedUnderAnIdDerivedFromTheGate()
    {
        (ScopeTestProject p, Guid stageId, _) = await StageProjectWithTasksAsync("bpm7", 1);
        Guid gate = await CreateStageGateAsync(p, stageId, ConditionType.Approval, GateTrigger.Complete,
            approvalType: ApprovalType.ManagerApproval);

        Assert.Equal(Features.Gates.GateApprovalIds.For(gate), await ApprovalIdAsync(gate));
        Assert.NotEqual(Features.Gates.GateApprovalIds.For(Guid.NewGuid()), Features.Gates.GateApprovalIds.For(gate));
        Assert.Equal(Features.Gates.GateApprovalIds.For(gate), Features.Gates.GateApprovalIds.For(gate));
    }

    // ---- helpers ----

    private async Task PublishAsync<TEvent>(TEvent @event) where TEvent : BuildingBlocks.Events.IIntegrationEvent
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<BuildingBlocks.Events.IEventBus>().PublishAsync(@event, CancellationToken.None);
    }

    private async Task<Guid> ApprovalIdAsync(Guid gateId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().GateConditions.AsNoTracking()
            .Where(g => g.Id == gateId).Select(g => g.ApprovalId!.Value).SingleAsync();
    }

    internal static void AssertError(Result result, string code, int status)
    {
        Assert.True(result.IsFailure, $"expected {code}, got success");
        Assert.Equal((code, status), (result.Error.Code, result.Error.Status));
    }

    private async Task<(ScopeTestProject Project, Guid StageId, Guid[] TaskIds)> StageProjectWithTasksAsync(
        string label, int taskCount, int stageCount = 1)
    {
        decimal?[] fees = [.. Enumerable.Repeat((decimal?)null, stageCount)];
        (Guid templateId, IReadOnlyList<SeededStage> stages) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, label, fees);
        foreach (SeededStage stage in stages)
        {
            await ScopeOfWorkSeeding.AddTasksAndDepartmentAsync(fixture, stage.Id, taskCount);
        }

        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, label, stageTemplateId: templateId);

        await using AsyncServiceScope scope = fixture.CreateScope();
        ProjectsDbContext db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        Guid firstStage = await db.ProjectStages.AsNoTracking().Where(s => s.ProjectId == p.ProjectId)
            .OrderBy(s => s.Order).Select(s => s.Id).FirstAsync();
        Guid[] taskIds = await db.ProjectTasks.AsNoTracking().Where(t => t.ProjectStageId == firstStage)
            .OrderBy(t => t.Order).Select(t => t.Id).ToArrayAsync();
        return (p, firstStage, taskIds);
    }

    private async Task<Guid> CreateTaskGateAsync(
        ScopeTestProject p, Guid taskId, ConditionType type, GateTrigger trigger,
        Guid? referencedTaskId = null, Guid? referencedStageId = null, ApprovalType? approvalType = null)
    {
        Result<Guid> gate = await RunAsync<CreateTaskGateConditionCommand, Guid>(p, new CreateTaskGateConditionCommand(
            p.ProjectId, taskId, p.OfficeUserId, type, trigger, $"{type} gate",
            referencedTaskId, referencedStageId, approvalType));
        Assert.True(gate.IsSuccess, gate.IsFailure ? gate.Error.ToString() : null);
        return gate.Value;
    }

    private async Task<Guid> CreateStageGateAsync(
        ScopeTestProject p, Guid stageId, ConditionType type, GateTrigger trigger,
        Guid? referencedTaskId = null, Guid? referencedStageId = null, ApprovalType? approvalType = null)
    {
        Result<Guid> gate = await RunAsync<CreateStageGateConditionCommand, Guid>(p, new CreateStageGateConditionCommand(
            p.ProjectId, stageId, p.OfficeUserId, type, trigger, $"{type} gate",
            referencedTaskId, referencedStageId, approvalType));
        Assert.True(gate.IsSuccess, gate.IsFailure ? gate.Error.ToString() : null);
        return gate.Value;
    }

    /// <summary>An office scope that also holds the stage and task gate permissions the gate handlers check.</summary>
    private AsyncServiceScope GateScope(ScopeTestProject p, params string[] permissions)
    {
        AsyncServiceScope scope = ScopeOfWorkSeeding.OfficeScope(fixture, p);
        TestCurrentUser user = scope.ServiceProvider.GetRequiredService<TestCurrentUser>();
        foreach (string permission in permissions.Length == 0
                     ? [ProjectsPermissions.Stages.ManageGates, ProjectsPermissions.StageTasks.ManageGates, ProjectsPermissions.StageTasks.Manage]
                     : permissions)
        {
            user.Grant(permission);
        }

        return scope;
    }

    private async Task<Result> RunAsync<TCommand>(ScopeTestProject p, TCommand command, params string[] permissions)
        where TCommand : ICommand
    {
        await using AsyncServiceScope scope = GateScope(p, permissions);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(command);
    }

    private async Task<Result<TResponse>> RunAsync<TCommand, TResponse>(ScopeTestProject p, TCommand command)
        where TCommand : ICommand<TResponse>
    {
        await using AsyncServiceScope scope = GateScope(p);
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>().HandleAsync(command);
    }

    private async Task ExpectOkAsync<TCommand>(ScopeTestProject p, TCommand command) where TCommand : ICommand
    {
        Result result = await RunAsync(p, command);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
    }

    private async Task<Domain.GateStatus> GateStatusAsync(Guid gateId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().GateConditions.AsNoTracking()
            .Where(g => g.Id == gateId).Select(g => g.Status).SingleAsync();
    }

    private async Task<Domain.ProjectTaskStatus> TaskStatusAsync(Guid taskId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().ProjectTasks.AsNoTracking()
            .Where(t => t.Id == taskId).Select(t => t.Status).SingleAsync();
    }

    private async Task<Domain.StageStatus> StageStatusAsync(Guid stageId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().ProjectStages.AsNoTracking()
            .Where(s => s.Id == stageId).Select(s => s.Status).SingleAsync();
    }

    /// <summary>Starts the task and completes its sub-tasks, so it can be completed.</summary>
    private async Task ReadyAsync(ScopeTestProject p, Guid taskId)
    {
        await ExpectOkAsync(p, new StartProjectTaskCommand(p.ProjectId, taskId, p.OfficeUserId));
        await using AsyncServiceScope scope = fixture.CreateScope();
        List<Guid> subTasks = await scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().ProjectSubTasks.AsNoTracking()
            .Where(s => s.ProjectTaskId == taskId).Select(s => s.Id).ToListAsync();
        foreach (Guid subTask in subTasks)
        {
            await ExpectOkAsync(p, new CompleteProjectSubTaskCommand(p.ProjectId, subTask, p.OfficeUserId));
        }
    }
}
