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

    /// <summary>Approver pick: with two teams on a stage the approver is the head of the stage's first team (created
    /// first, then by id), whatever order Postgres returns the rows in — here the first team's row is rewritten, which
    /// moves it behind the second in the heap.</summary>
    [Fact]
    public async Task ApproverPick_IsTheHeadOfTheStagesFirstTeam_Deterministically()
    {
        (ScopeTestProject p, Guid stageId, _) = await StageProjectWithTasksAsync("approver", 1);
        string firstHeadUser;
        string secondHeadUser;
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            IServiceProvider sp = scope.ServiceProvider;
            ProjectsDbContext db = sp.GetRequiredService<ProjectsDbContext>();
            Domain.StageTeam first = await db.StageTeams.SingleAsync(t => t.ProjectStageId == stageId);
            (Guid secondHead, secondHeadUser) = await HrTestSeeding.HireEmployeeAsync(sp, $"second-head-{Guid.NewGuid():N}@example.com", lastName: "Second");
            db.StageTeams.Add(Domain.StageTeam.CreateProjectStandaloneTeam(stageId, secondHead));
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE projects.stage_teams SET \"HeadId\" = \"HeadId\" WHERE \"Id\" = {first.Id}");
            firstHeadUser = (await sp.GetRequiredService<Hr.Contracts.IHrDirectory>().GetAsync(first.HeadId, CancellationToken.None))!.AppUserId!;
        }

        Guid gate = await CreateStageGateAsync(p, stageId, ConditionType.Approval, GateTrigger.Complete,
            approvalType: ApprovalType.ManagerApproval);

        await using AsyncServiceScope read = fixture.CreateScope();
        Guid approvalId = await ApprovalIdAsync(gate);
        List<string> approvers = await read.ServiceProvider.GetRequiredService<ProjectsDbContext>().Database
            .SqlQuery<string>($"SELECT \"ApproverUserId\" AS \"Value\" FROM approvals.approval_approvers WHERE approval_id = {approvalId}")
            .ToListAsync();
        Assert.Contains(firstHeadUser, approvers);
        Assert.DoesNotContain(secondHeadUser, approvers);
    }

    /// <summary>P5: a scope-of-work write on a template-created project (stages, no scope) is 409
    /// <c>projects.scope.ProjectHasStages</c> instead of creating a Draft that can never be confirmed; and a project
    /// already stuck with such a Draft counts as settled again, so manual stage edits work.</summary>
    [Fact]
    public async Task P5_AScopeWriteOnAProjectWithStages_IsRefused_AndAStuckDraftCountsAsSettled()
    {
        (ScopeTestProject p, _, _) = await StageProjectWithTasksAsync("p5", 1);

        Result header = await RunAsync(p, new Contracts.Scope.UpdateScopeOfWorkHeaderCommand(
            p.ProjectId, p.OfficeUserId, 30, null, 0m, null, "Too late"));
        AssertError(header, "projects.scope.ProjectHasStages", 409);

        // A row stuck by the old behaviour: a Draft scope beside template stages.
        await using (AsyncServiceScope scope = fixture.CreateScope())
        {
            ProjectsDbContext db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();
            if (!await db.ScopesOfWork.AnyAsync(s => s.ProjectId == p.ProjectId))
            {
                db.ScopesOfWork.Add(Domain.ScopeOfWork.Create(p.ProjectId));
                await db.SaveChangesAsync();
            }
        }

        Result<Guid> stage = await RunAsync<CreateProjectStageCommand, Guid>(p, new CreateProjectStageCommand(
            p.ProjectId, p.OfficeUserId, "Added by hand", 2, null, null, null, null, null));
        Assert.True(stage.IsSuccess, stage.IsFailure ? stage.Error.ToString() : null);
    }

    /// <summary>P11: task cancel, reject and reopen and stage complete and reject take the row lock before reading, so
    /// a transition racing another writer of the same row waits and then sees its result (409), instead of reading
    /// the old state and overwriting the other write.</summary>
    [Fact]
    public async Task P11_TransitionsRacingAnotherWriter_WaitAndSeeItsResult()
    {
        (ScopeTestProject p, Guid stageId, Guid[] tasks) = await StageProjectWithTasksAsync("p11", 3);
        int completed = (int)Domain.ProjectTaskStatus.Completed;
        int open = (int)Domain.ProjectTaskStatus.Open;

        AssertError(await RaceAsync(
            $"UPDATE projects.project_tasks SET \"Status\" = {completed} WHERE \"Id\" = '{tasks[0]}'",
            () => RunAsync(p, new CancelProjectTaskCommand(p.ProjectId, tasks[0], p.OfficeUserId))),
            "tasks.InvalidStatusTransition", 409);

        await ExpectOkAsync(p, new StartProjectTaskCommand(p.ProjectId, tasks[1], p.OfficeUserId));
        AssertError(await RaceAsync(
            $"UPDATE projects.project_tasks SET \"Status\" = {open} WHERE \"Id\" = '{tasks[1]}'",
            () => RunAsync(p, new RejectProjectTaskCommand(p.ProjectId, tasks[1], p.OfficeUserId, "No"))),
            "tasks.InvalidStatusTransition", 409);

        await ExpectOkAsync(p, new CancelProjectTaskCommand(p.ProjectId, tasks[2], p.OfficeUserId));
        AssertError(await RaceAsync(
            $"UPDATE projects.project_tasks SET \"Status\" = {open} WHERE \"Id\" = '{tasks[2]}'",
            () => RunAsync(p, new ReopenProjectTaskCommand(p.ProjectId, tasks[2], p.OfficeUserId))),
            "tasks.InvalidStatusTransition", 409);

        AssertError(await RaceAsync(
            $"UPDATE projects.project_stages SET \"Status\" = {(int)Domain.StageStatus.Rejected} WHERE \"Id\" = '{stageId}'",
            () => RunAsync(p, new RejectProjectStageCommand(p.ProjectId, stageId, p.OfficeUserId, "No"))),
            "stages.InvalidStatusTransition", 409);
        AssertError(await RaceAsync(
            $"UPDATE projects.project_stages SET \"Status\" = {(int)Domain.StageStatus.Completed} WHERE \"Id\" = '{stageId}'",
            () => RunAsync(p, new CompleteProjectStageCommand(p.ProjectId, stageId, p.OfficeUserId))),
            "stages.InvalidStatusTransition", 409);
    }

    /// <summary>P14: an optional sub-task left open does not block completing its task; a required one does.</summary>
    [Fact]
    public async Task P14_OnlyRequiredSubTasks_BlockTaskCompletion()
    {
        (ScopeTestProject p, _, Guid[] tasks) = await StageProjectWithTasksAsync("p14", 2);

        Result<Guid> optional = await RunAsync<CreateProjectSubTaskCommand, Guid>(p, new CreateProjectSubTaskCommand(
            p.ProjectId, tasks[0], p.OfficeUserId, "Nice to have", null, null, IsRequired: false));
        Assert.True(optional.IsSuccess, optional.IsFailure ? optional.Error.ToString() : null);
        await ReadyAsync(p, tasks[0], requiredOnly: true);
        await ExpectOkAsync(p, new CompleteProjectTaskCommand(p.ProjectId, tasks[0], p.OfficeUserId));

        await ExpectOkAsync(p, new StartProjectTaskCommand(p.ProjectId, tasks[1], p.OfficeUserId));
        AssertError(await RunAsync(p, new CompleteProjectTaskCommand(p.ProjectId, tasks[1], p.OfficeUserId)),
            "tasks.SubTasksIncomplete", 409);
    }

    /// <summary>NP7: a stage completes only when each of its tasks is Completed or Cancelled; otherwise 409
    /// <c>stages.TasksOpen</c>.</summary>
    [Fact]
    public async Task NP7_AStageWithAnUnfinishedTask_DoesNotComplete()
    {
        (ScopeTestProject p, Guid stageId, Guid[] tasks) = await StageProjectWithTasksAsync("np7", 2);
        await ReadyAsync(p, tasks[0]);
        await ExpectOkAsync(p, new CompleteProjectTaskCommand(p.ProjectId, tasks[0], p.OfficeUserId));

        AssertError(await RunAsync(p, new CompleteProjectStageCommand(p.ProjectId, stageId, p.OfficeUserId)),
            "stages.TasksOpen", 409);
        Assert.Equal(Domain.StageStatus.Active, await StageStatusAsync(stageId));

        await ExpectOkAsync(p, new CancelProjectTaskCommand(p.ProjectId, tasks[1], p.OfficeUserId));
        await ExpectOkAsync(p, new CompleteProjectStageCommand(p.ProjectId, stageId, p.OfficeUserId));
        Assert.Equal(Domain.StageStatus.Completed, await StageStatusAsync(stageId));
    }

    /// <summary>NP3: an on-hold, completed, cancelled or inactive project refuses every work write with 409
    /// <c>projects.Closed</c> (tasks, sub-tasks, stages, gates, scope, teams), and is marked Completed only once no
    /// stage is open (409 <c>projects.StagesOpen</c>).</summary>
    [Fact]
    public async Task NP3_AClosedProject_RefusesWorkWrites_AndCompletesOnlyWithNoOpenStage()
    {
        (ScopeTestProject p, Guid stageId, Guid[] tasks) = await StageProjectWithTasksAsync("np3", 1);
        Guid subTask = await SubTaskOfAsync(tasks[0]);
        await ExpectOkAsync(p, new Contracts.Projects.ChangeProjectStatusCommand(p.ProjectId, p.OfficeUserId, ProjectStatus.InProgress));

        AssertError(await RunAsync(p, new Contracts.Projects.ChangeProjectStatusCommand(p.ProjectId, p.OfficeUserId, ProjectStatus.Completed)),
            "projects.StagesOpen", 409);

        await ExpectOkAsync(p, new Contracts.Projects.ChangeProjectStatusCommand(p.ProjectId, p.OfficeUserId, ProjectStatus.OnHold));
        await AssertClosedAsync(p, stageId, tasks[0], subTask);

        await ExpectOkAsync(p, new Contracts.Projects.ChangeProjectStatusCommand(p.ProjectId, p.OfficeUserId, ProjectStatus.InProgress));
        await ExpectOkAsync(p, new StartProjectTaskCommand(p.ProjectId, tasks[0], p.OfficeUserId));

        await ExpectOkAsync(p, new Contracts.Projects.SetProjectActiveCommand(p.ProjectId, p.OfficeUserId, false));
        AssertError(await RunAsync(p, new CancelProjectTaskCommand(p.ProjectId, tasks[0], p.OfficeUserId)), "projects.Closed", 409);
    }

    /// <summary>R1-3: the authorization order is route claim (403) → scope (404) → actor (403) → state (409), and a
    /// closed project is state. On a project on hold, a missing child id is still its 404, and a caller without the
    /// gate's own claim or not the sub-task's assignee is still 403 — not the closed project's 409.</summary>
    [Fact]
    public async Task R1_3_OnAClosedProject_AMissingChildIs404_AndAMissingClaimIs403()
    {
        (ScopeTestProject p, _, Guid[] tasks) = await StageProjectWithTasksAsync("r1-3", 1);
        Guid subTask = await SubTaskOfAsync(tasks[0]);
        Guid taskGate = await CreateTaskGateAsync(p, tasks[0], ConditionType.ManualCheck, GateTrigger.Complete);
        await ExpectOkAsync(p, new Contracts.Projects.ChangeProjectStatusCommand(p.ProjectId, p.OfficeUserId, ProjectStatus.InProgress));
        await ExpectOkAsync(p, new Contracts.Projects.ChangeProjectStatusCommand(p.ProjectId, p.OfficeUserId, ProjectStatus.OnHold));
        var missing = Guid.NewGuid();

        AssertError(await RunAsync(p, new StartProjectTaskCommand(p.ProjectId, missing, p.OfficeUserId)), "tasks.NotFound", 404);
        AssertError(await RunAsync(p, new CompleteProjectTaskCommand(p.ProjectId, missing, p.OfficeUserId)), "tasks.NotFound", 404);
        AssertError(await RunAsync(p, new ReopenProjectTaskCommand(p.ProjectId, missing, p.OfficeUserId)), "tasks.NotFound", 404);
        AssertError(await RunAsync(p, new CompleteProjectSubTaskCommand(p.ProjectId, missing, p.OfficeUserId)), "subtasks.NotFound", 404);
        AssertError(await RunAsync(p, new UpdateProjectSubTaskCommand(p.ProjectId, tasks[0], missing, p.OfficeUserId, "Renamed", null, null, null)),
            "subtasks.NotFound", 404);
        AssertError(await RunAsync(p, new UpdateProjectTaskCommand(p.ProjectId, missing, p.OfficeUserId, "Renamed", null, null, null, null, null, null)),
            "tasks.NotFound", 404);
        AssertError(await RunAsync(p, new UpdateProjectStageCommand(p.ProjectId, missing, p.OfficeUserId, "Renamed", null, null, null, null, null)),
            "stages.NotFound", 404);
        AssertError(await RunAsync(p, new RejectProjectStageCommand(p.ProjectId, missing, p.OfficeUserId, "No")), "stages.NotFound", 404);
        AssertError(await RunAsync(p, new DeleteGateConditionCommand(p.ProjectId, missing, p.OfficeUserId)), "gate_conditions.NotFound", 404);
        AssertError(await RunAsync<CreateTaskGateConditionCommand, Guid>(p, new CreateTaskGateConditionCommand(
            p.ProjectId, missing, p.OfficeUserId, ConditionType.ManualCheck, GateTrigger.Complete, "Late gate")), "tasks.NotFound", 404);
        AssertError(await RunAsync(p, new Contracts.Scope.UpdateScopeOfWorkItemCommand(p.ProjectId, p.OfficeUserId, missing, "Item", null, 0m)),
            "projects.scope.ItemNotFound", 404);

        // A task gate needs stage_tasks.manage_gates; the stage claim alone is 403 before the 409.
        AssertError(await RunAsync(p, new SatisfyGateConditionCommand(p.ProjectId, taskGate, p.OfficeUserId),
            ProjectsPermissions.Stages.ManageGates), "projects.AccessDenied", 403);
        AssertError(await RunAsync(p, new DeleteGateConditionCommand(p.ProjectId, taskGate, p.OfficeUserId),
            ProjectsPermissions.Stages.ManageGates), "projects.AccessDenied", 403);

        // Not the task's assignee and no stage_tasks.manage: 403 before the 409.
        AssertError(await RunAsync(p, new CompleteProjectSubTaskCommand(p.ProjectId, subTask, p.OfficeUserId),
            ProjectsPermissions.StageTasks.Complete), "projects.subtasks.NotAssignee", 403);

        // With every 404 and 403 passed, the closed project is the 409.
        AssertError(await RunAsync(p, new SatisfyGateConditionCommand(p.ProjectId, taskGate, p.OfficeUserId)), "projects.Closed", 409);
        AssertError(await RunAsync(p, new StartProjectTaskCommand(p.ProjectId, tasks[0], p.OfficeUserId)), "projects.Closed", 409);
    }

    private async Task AssertClosedAsync(ScopeTestProject p, Guid stageId, Guid taskId, Guid subTaskId)
    {
        const string closed = "projects.Closed";
        AssertError(await RunAsync(p, new StartProjectTaskCommand(p.ProjectId, taskId, p.OfficeUserId)), closed, 409);
        AssertError(await RunAsync(p, new CompleteProjectSubTaskCommand(p.ProjectId, subTaskId, p.OfficeUserId)), closed, 409);
        AssertError(await RunAsync(p, new UpdateProjectSubTaskCommand(p.ProjectId, taskId, subTaskId, p.OfficeUserId, "Renamed", null, null, null)), closed, 409);
        AssertError(await RunAsync(p, new RejectProjectStageCommand(p.ProjectId, stageId, p.OfficeUserId, "No")), closed, 409);
        AssertError(await RunAsync<CreateProjectTaskCommand, Guid>(p, new CreateProjectTaskCommand(
            p.ProjectId, p.OfficeUserId, stageId, "Late task", null, null, null, TaskPriority.Medium, 1m, null, null)), closed, 409);
        AssertError(await RunAsync<CreateProjectStageCommand, Guid>(p, new CreateProjectStageCommand(
            p.ProjectId, p.OfficeUserId, "Late stage", 2, null, null, null, null, null)), closed, 409);
        AssertError(await RunAsync<CreateTaskGateConditionCommand, Guid>(p, new CreateTaskGateConditionCommand(
            p.ProjectId, taskId, p.OfficeUserId, ConditionType.ManualCheck, GateTrigger.Complete, "Late gate")), closed, 409);
        AssertError(await RunAsync<Contracts.Teams.CreateStageTeamCommand, Guid>(p, new Contracts.Teams.CreateStageTeamCommand(
            p.ProjectId, p.OfficeUserId, stageId, null, p.OfficeEmployeeId)), closed, 409);
        AssertError(await RunAsync(p, new Contracts.Scope.UpdateScopeOfWorkHeaderCommand(
            p.ProjectId, p.OfficeUserId, 30, null, 0m, null, null)), closed, 409);
    }

    private async Task<Guid> SubTaskOfAsync(Guid taskId)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().ProjectSubTasks.AsNoTracking()
            .Where(s => s.ProjectTaskId == taskId).Select(s => s.Id).FirstAsync();
    }

    /// <summary>NP8: a status change stamps the project's actual start and end from <see cref="TimeProvider"/>, not
    /// the wall clock.</summary>
    [Fact]
    public async Task NP8_AStatusChange_StampsItsDatesFromTheTimeProvider()
    {
        ScopeTestProject p = await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "np8", WorkflowType.FreeStyle);
        var start = new DateTimeOffset(2031, 3, 4, 5, 6, 7, TimeSpan.Zero);
        var end = new DateTimeOffset(2032, 8, 9, 10, 11, 12, TimeSpan.Zero);

        await ChangeStatusAtAsync(p, ProjectStatus.InProgress, start);
        await ChangeStatusAtAsync(p, ProjectStatus.Completed, end);

        await using AsyncServiceScope scope = fixture.CreateScope();
        Domain.Project project = await scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().Projects.AsNoTracking()
            .SingleAsync(x => x.Id == p.ProjectId);
        Assert.Equal((start.UtcDateTime, end.UtcDateTime), (project.ActualStartDate!.Value, project.ActualEndDate!.Value));
    }

    private async Task ChangeStatusAtAsync(ScopeTestProject p, ProjectStatus status, DateTimeOffset now)
    {
        await using AsyncServiceScope scope = GateScope(p);
        Features.Projects.ChangeProjectStatusCommandHandler handler = ActivatorUtilities.CreateInstance<Features.Projects.ChangeProjectStatusCommandHandler>(
            scope.ServiceProvider, new FixedTimeProvider(now));
        Result result = await handler.HandleAsync(new Contracts.Projects.ChangeProjectStatusCommand(p.ProjectId, p.OfficeUserId, status));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.ToString() : null);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Sub-task complete gets reopen's checks: only the task's assignee or a task manager
    /// (<c>stage_tasks.manage</c>) may tick it (403 <c>projects.subtasks.NotAssignee</c>), and a completed or cancelled
    /// task's sub-tasks are frozen (409 <c>projects.subtasks.TaskClosed</c>).</summary>
    [Fact]
    public async Task SubTaskComplete_NeedsTheAssigneeOrAManager_AndAnOpenTask()
    {
        (ScopeTestProject p, _, Guid[] tasks) = await StageProjectWithTasksAsync("subtask", 1);
        Guid required = await SubTaskOfAsync(tasks[0]);

        AssertError(await RunAsync(p, new CompleteProjectSubTaskCommand(p.ProjectId, required, p.OfficeUserId),
            ProjectsPermissions.StageTasks.Complete), "projects.subtasks.NotAssignee", 403);

        await ExpectOkAsync(p, new AssignProjectTaskCommand(p.ProjectId, tasks[0], p.OfficeUserId, p.OfficeEmployeeId));
        Result asAssignee = await RunAsync(p, new CompleteProjectSubTaskCommand(p.ProjectId, required, p.OfficeUserId),
            ProjectsPermissions.StageTasks.Complete);
        Assert.True(asAssignee.IsSuccess, asAssignee.IsFailure ? asAssignee.Error.ToString() : null);

        Result<Guid> optional = await RunAsync<CreateProjectSubTaskCommand, Guid>(p, new CreateProjectSubTaskCommand(
            p.ProjectId, tasks[0], p.OfficeUserId, "Optional", null, null, IsRequired: false));
        await ExpectOkAsync(p, new StartProjectTaskCommand(p.ProjectId, tasks[0], p.OfficeUserId));
        await ExpectOkAsync(p, new CompleteProjectTaskCommand(p.ProjectId, tasks[0], p.OfficeUserId));
        AssertError(await RunAsync(p, new CompleteProjectSubTaskCommand(p.ProjectId, optional.Value, p.OfficeUserId)),
            "projects.subtasks.TaskClosed", 409);
    }

    /// <summary><c>WorkDeletionGuard.LockAsync</c> takes all the task rows in one <c>FOR UPDATE</c> statement, so the
    /// statement count no longer grows with the stage's task count.</summary>
    [Fact]
    public async Task WorkDeletionGuardLock_IsOneStatementForAllTasks()
    {
        (_, Guid oneStage, Guid[] one) = await StageProjectWithTasksAsync("lock-one", 1);
        (_, Guid manyStage, Guid[] many) = await StageProjectWithTasksAsync("lock-many", 6);

        int small = await CountLockStatementsAsync(oneStage, one);
        int large = await CountLockStatementsAsync(manyStage, many);

        Assert.Equal(small, large);
        Assert.Equal(2, large);
    }

    private async Task<int> CountLockStatementsAsync(Guid stageId, Guid[] taskIds)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        ProjectsDbContext db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx = await db.Database.BeginTransactionAsync();
        var guard = new Features.Stages.WorkDeletionGuard(db, null!, null!, null!);
        DbCommandCounter.Counts counts = DbCommandCounter.Begin();
        try
        {
            await guard.LockAsync(stageId, taskIds, CancellationToken.None);
        }
        finally
        {
            DbCommandCounter.End();
        }

        await tx.RollbackAsync();
        return counts.Queries;
    }

    /// <summary>Template delete: 409 <c>stage_templates.InUse</c> while a project stage was made from it; otherwise its
    /// stages, tasks, sub-tasks and departments go with it. A duplicate name is 409 <c>stage_templates.NameTaken</c>
    /// on create and update (not a 500 from the unique index), and an update is validated.</summary>
    [Fact]
    public async Task TemplateDelete_RefusesAUsedTemplate_RemovesChildren_AndNamesAreUnique()
    {
        (Guid used, _) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "tpl-used", (decimal?)null);
        await ScopeOfWorkSeeding.CreateProjectAsync(fixture, "tpl-used", stageTemplateId: used);
        (Guid unused, IReadOnlyList<SeededStage> stages) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "tpl-free", (decimal?)null);
        await ScopeOfWorkSeeding.AddTasksAndDepartmentAsync(fixture, stages[0].Id, 2);

        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        ICommandHandler<Contracts.Templates.DeleteStageTemplateCommand> delete =
            sp.GetRequiredService<ICommandHandler<Contracts.Templates.DeleteStageTemplateCommand>>();

        AssertError(await delete.HandleAsync(new Contracts.Templates.DeleteStageTemplateCommand(used)), "stage_templates.InUse", 409);

        Result deleted = await delete.HandleAsync(new Contracts.Templates.DeleteStageTemplateCommand(unused));
        Assert.True(deleted.IsSuccess, deleted.IsFailure ? deleted.Error.ToString() : null);
        ProjectsDbContext db = sp.GetRequiredService<ProjectsDbContext>();
        Assert.False(await db.Stages.AnyAsync(x => x.StageTemplateId == unused));
        Assert.False(await db.StageTasks.AnyAsync(x => x.StageId == stages[0].Id));
        Assert.False(await db.StageDepartments.AnyAsync(x => x.StageId == stages[0].Id));

        string name = await db.StageTemplates.Where(t => t.Id == used).Select(t => t.Name).SingleAsync();
        await using AsyncServiceScope second = fixture.CreateScope();
        AssertError(await second.ServiceProvider.GetRequiredService<ICommandHandler<Contracts.Templates.CreateStageTemplateCommand, Guid>>()
            .HandleAsync(new Contracts.Templates.CreateStageTemplateCommand(name, StageTemplateType.Custom)), "stage_templates.NameTaken", 409);

        (Guid other, _) = await ScopeOfWorkSeeding.CreateTemplateAsync(fixture, "tpl-other");
        await using AsyncServiceScope third = fixture.CreateScope();
        ICommandHandler<Contracts.Templates.UpdateStageTemplateCommand> update =
            third.ServiceProvider.GetRequiredService<ICommandHandler<Contracts.Templates.UpdateStageTemplateCommand>>();
        AssertError(await update.HandleAsync(new Contracts.Templates.UpdateStageTemplateCommand(other, Name: name)), "stage_templates.NameTaken", 409);
        Result tooLong = await update.HandleAsync(new Contracts.Templates.UpdateStageTemplateCommand(other, Name: new string('t', 201)));
        Assert.True(tooLong.IsFailure);
        Assert.Equal(400, tooLong.Error.Status);
    }

    /// <summary>Deadline jobs: a fixed number of queries whatever the number of due tasks or stages; the due date is in
    /// the dedupe key (a moved deadline reminds again); and work of an inactive or closed project is skipped.</summary>
    [Fact]
    public async Task DeadlineJobs_AreBatched_KeyOnTheDueDate_AndSkipClosedProjects()
    {
        var now = new DateTimeOffset(2041, 5, 6, 9, 0, 0, TimeSpan.Zero);
        DateTime due = now.UtcDateTime.Date.AddDays(1);
        (ScopeTestProject live, Guid liveStage, Guid[] liveTasks) = await StageProjectWithTasksAsync("deadline-live", 6);
        (ScopeTestProject closed, Guid closedStage, _) = await StageProjectWithTasksAsync("deadline-closed", 1);
        await ExpectOkAsync(closed, new Contracts.Projects.ChangeProjectStatusCommand(closed.ProjectId, closed.OfficeUserId, ProjectStatus.Cancelled));
        await SetDueAsync(closed.ProjectId, due);

        await SetDueAsync(live.ProjectId, due, liveTasks[..1]);
        (int oneTaskQueries, List<Notified> first) = await RunJobAsync<Features.Jobs.TaskDeadlineJob>(now);
        await SetDueAsync(live.ProjectId, due);
        (int sixTaskQueries, List<Notified> tasks) = await RunJobAsync<Features.Jobs.TaskDeadlineJob>(now);
        (_, List<Notified> stages) = await RunJobAsync<Features.Jobs.StageDeadlineJob>(now);

        bool IsLive(Notified n) => n.EntityId is { } id && liveTasks.Contains(id);
        Assert.Equal(oneTaskQueries, sixTaskQueries);
        Assert.Single(first, IsLive);
        Assert.Equal(6, tasks.Count(IsLive));
        Assert.All(tasks.Where(IsLive), n => Assert.Contains($"{due:yyyyMMdd}", n.DedupeKey));
        Assert.Contains(stages, n => n.EntityId == liveStage && n.DedupeKey!.Contains($"{due:yyyyMMdd}"));
        Assert.DoesNotContain(stages, n => n.EntityId == closedStage);
        await using AsyncServiceScope scope = fixture.CreateScope();
        List<Guid> closedTasks = await scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().ProjectTasks.AsNoTracking()
            .Where(t => t.ProjectId == closed.ProjectId).Select(t => t.Id).ToListAsync();
        Assert.DoesNotContain(tasks, n => n.EntityId is { } id && closedTasks.Contains(id));
    }

    private sealed record Notified(Guid? EntityId, string? DedupeKey);

    private sealed class RecordingNotifications : Notifications.Contracts.INotificationService
    {
        public List<Notified> Sent { get; } = [];

        public Task NotifyAsync(string recipientUserId, Notifications.Contracts.NotificationType type, string title, string? body = null,
            Notifications.Contracts.NotificationEntityType? entityType = null, Guid? entityId = null, string? dedupeKey = null,
            CancellationToken ct = default)
        {
            Sent.Add(new Notified(entityId, dedupeKey));
            return Task.CompletedTask;
        }

        public Task NotifyManyAsync(IReadOnlyCollection<string> recipientUserIds, Notifications.Contracts.NotificationType type, string title,
            string? body = null, Notifications.Contracts.NotificationEntityType? entityType = null, Guid? entityId = null,
            string? dedupeKey = null, CancellationToken ct = default)
        {
            Sent.Add(new Notified(entityId, dedupeKey));
            return Task.CompletedTask;
        }
    }

    private async Task<(int Queries, List<Notified> Sent)> RunJobAsync<TJob>(DateTimeOffset now)
        where TJob : BuildingBlocks.Jobs.IPeriodicJob
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        var notifications = new RecordingNotifications();
        TJob job = ActivatorUtilities.CreateInstance<TJob>(scope.ServiceProvider, notifications, new FixedTimeProvider(now));
        DbCommandCounter.Counts counts = DbCommandCounter.Begin();
        try
        {
            await job.RunAsync(CancellationToken.None);
        }
        finally
        {
            DbCommandCounter.End();
        }

        return (counts.Queries, notifications.Sent);
    }

    private async Task SetDueAsync(Guid projectId, DateTime due, Guid[]? onlyTasks = null)
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        ProjectsDbContext db = scope.ServiceProvider.GetRequiredService<ProjectsDbContext>();
        Guid[] tasks = onlyTasks ?? await db.ProjectTasks.Where(t => t.ProjectId == projectId).Select(t => t.Id).ToArrayAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE projects.project_tasks SET \"DueDate\" = {due} WHERE \"Id\" = ANY({tasks})");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE projects.project_stages SET \"EstimatedEndDate\" = {due} WHERE \"ProjectId\" = {projectId}");
    }

    // ---- helpers ----

    /// <summary>Runs <paramref name="update"/> in an open transaction that holds the row, starts
    /// <paramref name="transition"/>, lets it run into the row, then commits the update and returns the transition's
    /// result.</summary>
    private async Task<Result> RaceAsync(string update, Func<Task<Result>> transition)
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using Npgsql.NpgsqlTransaction tx = await connection.BeginTransactionAsync();
        await using (var command = new Npgsql.NpgsqlCommand(update, connection, tx))
        {
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        Task<Result> racing = Task.Run(transition);
        await Task.Delay(700);
        await tx.CommitAsync();
        return await racing;
    }

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
    private async Task ReadyAsync(ScopeTestProject p, Guid taskId, bool requiredOnly = false)
    {
        await ExpectOkAsync(p, new StartProjectTaskCommand(p.ProjectId, taskId, p.OfficeUserId));
        await using AsyncServiceScope scope = fixture.CreateScope();
        List<Guid> subTasks = await scope.ServiceProvider.GetRequiredService<ProjectsDbContext>().ProjectSubTasks.AsNoTracking()
            .Where(s => s.ProjectTaskId == taskId && (s.IsRequired || !requiredOnly)).Select(s => s.Id).ToListAsync();
        foreach (Guid subTask in subTasks)
        {
            await ExpectOkAsync(p, new CompleteProjectSubTaskCommand(p.ProjectId, subTask, p.OfficeUserId));
        }
    }
}
