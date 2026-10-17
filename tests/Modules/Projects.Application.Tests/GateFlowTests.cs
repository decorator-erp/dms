using Approvals.Contracts.Approvals;
using BuildingBlocks.Events;
using BuildingBlocks.Messaging;
using Hr.Contracts.Employees;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Gates;
using Projects.Contracts.Projects;
using Projects.Contracts.Stages;
using Projects.Contracts.Tasks;
using Projects.Contracts.Templates;
using SharedKernel;
using Xunit;

namespace Projects.Application.Tests;

/// <summary>
/// G5-08's own verify block: a gate cascade resolves in one command's transaction (the command-path
/// atomicity fix); the full approval-gate flow — create gate → approval requested → decide → outbox
/// delivers through the REAL <c>OutboxProcessor</c> path (not a direct in-process call to the cascade
/// logic) → gate satisfied → dependent unlocked; a <c>DesignBrief</c>-subject decision event does not
/// touch any gate (the <see cref="Approvals.Contracts.ApprovalSubjectType"/> filter, proven by reusing a
/// REAL gate's own id as the fake event's subject id — if the filter didn't work, this gate would flip).
/// </summary>
[Collection(ProjectsApplicationCollection.Name)]
public sealed class GateFlowTests(ProjectsApplicationFixture fixture)
{
    [Fact]
    public async Task TaskCompletion_SatisfiesReferencingGate_AndUnlocksDependentTask_InOneCommand()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
        (string? ownerUserId, Guid _) = await RegisterEmployeeAsync(sp, "cascade-owner");
        currentUser.UserId = ownerUserId;
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);

        (Guid projectId, IReadOnlyDictionary<string, Guid>? taskIds) = await CreateProjectWithTasksAsync(sp, ownerUserId, "Task A", "Task B");
        Guid taskAId = taskIds["Task A"];
        Guid taskBId = taskIds["Task B"];

        ICommandHandler<CreateTaskGateConditionCommand, Guid> createGate = sp.GetRequiredService<ICommandHandler<CreateTaskGateConditionCommand, Guid>>();
        Result<Guid> gateResult = await createGate.HandleAsync(new CreateTaskGateConditionCommand(
            projectId, taskBId, ownerUserId, ConditionType.Task, GateTrigger.Unlock, "Wait for Task A",
            ReferencedTaskId: taskAId));
        Assert.True(gateResult.IsSuccess);

        IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>> getTasks = sp.GetRequiredService<IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>>();
        IReadOnlyList<ProjectTaskResponse> afterGateCreated = (await getTasks.HandleAsync(new GetProjectTasksByProjectQuery(projectId, ownerUserId))).Value!;
        Assert.Equal(ProjectTaskStatus.Locked, afterGateCreated.Single(t => t.Id == taskBId).Status);

        ICommandHandler<StartProjectTaskCommand> start = sp.GetRequiredService<ICommandHandler<StartProjectTaskCommand>>();
        Assert.True((await start.HandleAsync(new StartProjectTaskCommand(projectId, taskAId, ownerUserId))).IsSuccess);

        // ONE command call - the gate satisfaction and Task B's unlock must both land inside this
        // transaction (G5-08's DomainEventDispatchDecorator fix), not as separate, crash-vulnerable saves.
        ICommandHandler<CompleteProjectTaskCommand> complete = sp.GetRequiredService<ICommandHandler<CompleteProjectTaskCommand>>();
        Result completeResult = await complete.HandleAsync(new CompleteProjectTaskCommand(projectId, taskAId, ownerUserId));
        Assert.True(completeResult.IsSuccess);

        IReadOnlyList<ProjectTaskResponse> afterCascade = (await getTasks.HandleAsync(new GetProjectTasksByProjectQuery(projectId, ownerUserId))).Value!;
        Assert.Equal(ProjectTaskStatus.Open, afterCascade.Single(t => t.Id == taskBId).Status);

        IQueryHandler<GetTaskGateConditionsQuery, IReadOnlyList<GateConditionResponse>> getGates = sp.GetRequiredService<IQueryHandler<GetTaskGateConditionsQuery, IReadOnlyList<GateConditionResponse>>>();
        IReadOnlyList<GateConditionResponse> gates = (await getGates.HandleAsync(new GetTaskGateConditionsQuery(projectId, taskBId, ownerUserId))).Value!;
        Assert.Equal(GateStatus.Satisfied, Assert.Single(gates).Status);
    }

    [Fact]
    public async Task ApprovalGate_RequestsThenDecidesThroughRealOutboxDelivery_SatisfiesGateAndUnlocksTask()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
        (string? ownerUserId, Guid _) = await RegisterEmployeeAsync(sp, "approval-owner");
        currentUser.UserId = ownerUserId;
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);

        (Guid projectId, IReadOnlyDictionary<string, Guid>? taskIds) = await CreateProjectWithTasksAsync(sp, ownerUserId, "Needs Approval");
        Guid taskId = taskIds["Needs Approval"];

        ICommandHandler<CreateTaskGateConditionCommand, Guid> createGate = sp.GetRequiredService<ICommandHandler<CreateTaskGateConditionCommand, Guid>>();
        Result<Guid> gateResult = await createGate.HandleAsync(new CreateTaskGateConditionCommand(
            projectId, taskId, ownerUserId, ConditionType.Approval, GateTrigger.Unlock, "Manager sign-off",
            ApprovalType: global::Projects.Contracts.ApprovalType.ManagerApproval));
        Assert.True(gateResult.IsSuccess);

        IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>> getTasks = sp.GetRequiredService<IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>>();
        Assert.Equal(ProjectTaskStatus.Locked,
            (await getTasks.HandleAsync(new GetProjectTasksByProjectQuery(projectId, ownerUserId))).Value!
                .Single(t => t.Id == taskId).Status);

        IQueryHandler<GetTaskGateConditionsQuery, IReadOnlyList<GateConditionResponse>> getGates = sp.GetRequiredService<IQueryHandler<GetTaskGateConditionsQuery, IReadOnlyList<GateConditionResponse>>>();
        GateConditionResponse gate = Assert.Single((await getGates.HandleAsync(new GetTaskGateConditionsQuery(projectId, taskId, ownerUserId))).Value!);
        Assert.Equal(GateStatus.Pending, gate.Status);
        Assert.NotNull(gate.ApprovalId);

        // Decide as the project owner - decision #1's approver resolution (owner + stage-team head, no
        // team here) makes the owner the sole eligible decider.
        ICommandHandler<DecideApprovalCommand> decide = sp.GetRequiredService<ICommandHandler<Approvals.Contracts.Approvals.DecideApprovalCommand>>();
        Result decideResult = await decide.HandleAsync(new Approvals.Contracts.Approvals.DecideApprovalCommand(
            gate.ApprovalId!.Value, Approvals.Contracts.ApprovalDecision.Approved, Comment: null));
        Assert.True(decideResult.IsSuccess);

        // Through the REAL delivery path - outbox row -> OutboxProcessor -> InboxDedupeDecorator ->
        // ApprovalDecidedIntegrationEventHandler - not a direct in-process call to the cascade logic.
        // This is the proof that F1 (integration-event path) and this task's own command-path fix carry
        // the cascade end to end, per G5-08's own verify text.
        await fixture.OutboxProcessor.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(ProjectTaskStatus.Open,
            (await getTasks.HandleAsync(new GetProjectTasksByProjectQuery(projectId, ownerUserId))).Value!
                .Single(t => t.Id == taskId).Status);

        GateConditionResponse gateAfter = Assert.Single((await getGates.HandleAsync(new GetTaskGateConditionsQuery(projectId, taskId, ownerUserId))).Value!);
        Assert.Equal(GateStatus.Satisfied, gateAfter.Status);
    }

    [Fact]
    public async Task ApprovalDecidedEvent_ForADesignBriefSubject_DoesNotTouchAnyGate()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
        (string? ownerUserId, Guid _) = await RegisterEmployeeAsync(sp, "filter-owner");
        currentUser.UserId = ownerUserId;
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);

        (Guid projectId, IReadOnlyDictionary<string, Guid>? taskIds) = await CreateProjectWithTasksAsync(sp, ownerUserId, "Untouched Task");
        Guid taskId = taskIds["Untouched Task"];

        ICommandHandler<CreateTaskGateConditionCommand, Guid> createGate = sp.GetRequiredService<ICommandHandler<CreateTaskGateConditionCommand, Guid>>();
        Guid gateId = (await createGate.HandleAsync(new CreateTaskGateConditionCommand(
            projectId, taskId, ownerUserId, ConditionType.ManualCheck, GateTrigger.Unlock, "Manual check"))).Value;

        // Reuses the REAL gate's own id as the fake event's SubjectId - if the SubjectType filter in
        // ApprovalDecidedIntegrationEventHandler didn't work, THIS gate would flip to Satisfied.
        IEventBus eventBus = sp.GetRequiredService<IEventBus>();
        await eventBus.PublishAsync(
            new Approvals.Contracts.ApprovalDecidedIntegrationEvent(
                Guid.NewGuid(), Guid.NewGuid(), Approvals.Contracts.ApprovalSubjectType.DesignBrief, gateId,
                Approvals.Contracts.ApprovalDecision.Approved, ownerUserId, DateTimeOffset.UtcNow),
            CancellationToken.None);

        IQueryHandler<GetTaskGateConditionsQuery, IReadOnlyList<GateConditionResponse>> getGates = sp.GetRequiredService<IQueryHandler<GetTaskGateConditionsQuery, IReadOnlyList<GateConditionResponse>>>();
        GateConditionResponse gate = Assert.Single((await getGates.HandleAsync(new GetTaskGateConditionsQuery(projectId, taskId, ownerUserId))).Value!);
        Assert.Equal(GateStatus.Pending, gate.Status);

        IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>> getTasks = sp.GetRequiredService<IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>>();
        Assert.Equal(ProjectTaskStatus.Locked,
            (await getTasks.HandleAsync(new GetProjectTasksByProjectQuery(projectId, ownerUserId))).Value!
                .Single(t => t.Id == taskId).Status);
    }

    /// <summary>Closes the last leg of the plan's own flow list ("template → instantiate → team assign →
    /// task lifecycle → approval gate → stage completion → next stage unlocked"): a Stage-type gate on
    /// Stage B referencing Stage A locks Stage B at creation; completing Stage A satisfies that gate and
    /// unlocks Stage B, all through the real command handlers.</summary>
    [Fact]
    public async Task StageCompletion_SatisfiesReferencingGate_AndUnlocksNextStage()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        TestCurrentUser currentUser = sp.GetRequiredService<TestCurrentUser>();
        (string? ownerUserId, Guid _) = await RegisterEmployeeAsync(sp, "next-stage-owner");
        currentUser.UserId = ownerUserId;
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);

        ICommandHandler<CreateStageTemplateCommand, Guid> createTemplate = sp.GetRequiredService<ICommandHandler<CreateStageTemplateCommand, Guid>>();
        Guid templateId = (await createTemplate.HandleAsync(
            new CreateStageTemplateCommand($"Gate-{Guid.NewGuid():N}", StageTemplateType.Custom))).Value;

        ICommandHandler<CreateStageCommand, Guid> createStage = sp.GetRequiredService<ICommandHandler<CreateStageCommand, Guid>>();
        Guid templateStageAId = (await createStage.HandleAsync(new CreateStageCommand(templateId, "Stage A", 1, 50m))).Value;
        Guid templateStageBId = (await createStage.HandleAsync(new CreateStageCommand(templateId, "Stage B", 2, 50m))).Value;

        Guid clientId = await CrmTestSeeding.SeedClientAsync(sp);
        ICommandHandler<CreateProjectCommand, Guid> createProject = sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>();
        Guid projectId = (await createProject.HandleAsync(new CreateProjectCommand(
            $"P-{Guid.NewGuid():N}", "Next Stage Test", "1 Test Rd", global::Projects.Contracts.WorkflowType.Stage,
            clientId, StageTemplateId: templateId))).Value;

        IQueryHandler<GetProjectStagesByProjectQuery, IReadOnlyList<ProjectStageResponse>> getStages = sp.GetRequiredService<IQueryHandler<GetProjectStagesByProjectQuery, IReadOnlyList<ProjectStageResponse>>>();
        IReadOnlyList<ProjectStageResponse> stages = (await getStages.HandleAsync(new GetProjectStagesByProjectQuery(projectId, ownerUserId))).Value!;
        ProjectStageResponse stageA = stages.Single(s => s.Name == "Stage A");
        ProjectStageResponse stageB = stages.Single(s => s.Name == "Stage B");

        ICommandHandler<CreateStageGateConditionCommand, Guid> createGate = sp.GetRequiredService<ICommandHandler<CreateStageGateConditionCommand, Guid>>();
        Result<Guid> gateResult = await createGate.HandleAsync(new CreateStageGateConditionCommand(
            projectId, stageB.Id, ownerUserId, ConditionType.Stage, GateTrigger.Unlock, "Wait for Stage A",
            ReferencedStageId: stageA.Id));
        Assert.True(gateResult.IsSuccess);

        IReadOnlyList<ProjectStageResponse> afterGateCreated = (await getStages.HandleAsync(new GetProjectStagesByProjectQuery(projectId, ownerUserId))).Value!;
        Assert.Equal(StageStatus.Locked, afterGateCreated.Single(s => s.Id == stageB.Id).Status);

        ICommandHandler<CompleteProjectStageCommand> complete = sp.GetRequiredService<ICommandHandler<CompleteProjectStageCommand>>();
        Result completeResult = await complete.HandleAsync(new CompleteProjectStageCommand(projectId, stageA.Id, ownerUserId));
        Assert.True(completeResult.IsSuccess);

        IReadOnlyList<ProjectStageResponse> afterCascade = (await getStages.HandleAsync(new GetProjectStagesByProjectQuery(projectId, ownerUserId))).Value!;
        Assert.Equal(StageStatus.Active, afterCascade.Single(s => s.Id == stageB.Id).Status);

        IQueryHandler<GetStageGateConditionsQuery, IReadOnlyList<GateConditionResponse>> getGates = sp.GetRequiredService<IQueryHandler<GetStageGateConditionsQuery, IReadOnlyList<GateConditionResponse>>>();
        IReadOnlyList<GateConditionResponse> gates = (await getGates.HandleAsync(new GetStageGateConditionsQuery(projectId, stageB.Id, ownerUserId))).Value!;
        Assert.Equal(GateStatus.Satisfied, Assert.Single(gates).Status);
    }

    private static async Task<(string UserId, Guid EmployeeId)> RegisterEmployeeAsync(IServiceProvider sp, string label)
    {
        string suffix = Guid.NewGuid().ToString("N");
        (Guid employeeId, string appUserId) = await HrTestSeeding.HireEmployeeAsync(
            sp, $"{label}-{suffix}@example.com", lastName: label);

        return (appUserId, employeeId);
    }

    /// <summary>One stage template with one <see cref="CreateStageTaskCommand"/> per name in
    /// <paramref name="taskNames"/> (in order), instantiated into a real project. No stage-team/gate
    /// wiring - gate tests build those themselves.</summary>
    private static async Task<(Guid ProjectId, IReadOnlyDictionary<string, Guid> TaskIdsByName)> CreateProjectWithTasksAsync(
        IServiceProvider sp, string ownerUserId, params string[] taskNames)
    {
        ICommandHandler<CreateStageTemplateCommand, Guid> createTemplate = sp.GetRequiredService<ICommandHandler<CreateStageTemplateCommand, Guid>>();
        Guid templateId = (await createTemplate.HandleAsync(
            new CreateStageTemplateCommand($"Gate-{Guid.NewGuid():N}", StageTemplateType.Custom))).Value;

        ICommandHandler<CreateStageCommand, Guid> createStage = sp.GetRequiredService<ICommandHandler<CreateStageCommand, Guid>>();
        Guid stageId = (await createStage.HandleAsync(new CreateStageCommand(templateId, "Only Stage", 1, 100m))).Value;

        ICommandHandler<CreateStageTaskCommand, Guid> createStageTask = sp.GetRequiredService<ICommandHandler<CreateStageTaskCommand, Guid>>();
        for (int i = 0; i < taskNames.Length; i++)
        {
            Assert.True((await createStageTask.HandleAsync(new CreateStageTaskCommand(stageId, taskNames[i], i + 1))).IsSuccess);
        }

        Guid clientId = await CrmTestSeeding.SeedClientAsync(sp);
        ICommandHandler<CreateProjectCommand, Guid> createProject = sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>();
        Guid projectId = (await createProject.HandleAsync(new CreateProjectCommand(
            $"P-{Guid.NewGuid():N}", "Gate Test Project", "1 Test Rd", global::Projects.Contracts.WorkflowType.Stage,
            clientId, StageTemplateId: templateId))).Value;

        IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>> getTasks = sp.GetRequiredService<IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>>();
        IReadOnlyList<ProjectTaskResponse> tasks = (await getTasks.HandleAsync(new GetProjectTasksByProjectQuery(projectId, ownerUserId))).Value!;

        return (projectId, tasks.ToDictionary(t => t.Name, t => t.Id));
    }
}
