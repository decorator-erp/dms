using BuildingBlocks.Events;
using BuildingBlocks.Messaging;
using Identity.Contracts.Auth;
using Hr.Contracts.Employees;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Gates;
using Projects.Contracts.Projects;
using Projects.Contracts.Tasks;
using Projects.Contracts.Templates;
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
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var currentUser = sp.GetRequiredService<TestCurrentUser>();
        var (ownerUserId, _) = await RegisterEmployeeAsync(sp, "cascade-owner");
        currentUser.UserId = ownerUserId;
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);

        var (projectId, taskIds) = await CreateProjectWithTasksAsync(sp, ownerUserId, "Task A", "Task B");
        var taskAId = taskIds["Task A"];
        var taskBId = taskIds["Task B"];

        var createGate = sp.GetRequiredService<ICommandHandler<CreateTaskGateConditionCommand, Guid>>();
        var gateResult = await createGate.HandleAsync(new CreateTaskGateConditionCommand(
            projectId, taskBId, ownerUserId, ConditionType.Task, GateTrigger.Unlock, "Wait for Task A",
            ReferencedTaskId: taskAId));
        Assert.True(gateResult.IsSuccess);

        var getTasks = sp.GetRequiredService<IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>>();
        var afterGateCreated = (await getTasks.HandleAsync(new GetProjectTasksByProjectQuery(projectId, ownerUserId))).Value!;
        Assert.Equal(ProjectTaskStatus.Locked, afterGateCreated.Single(t => t.Id == taskBId).Status);

        var start = sp.GetRequiredService<ICommandHandler<StartProjectTaskCommand>>();
        Assert.True((await start.HandleAsync(new StartProjectTaskCommand(projectId, taskAId, ownerUserId))).IsSuccess);

        // ONE command call - the gate satisfaction and Task B's unlock must both land inside this
        // transaction (G5-08's DomainEventDispatchDecorator fix), not as separate, crash-vulnerable saves.
        var complete = sp.GetRequiredService<ICommandHandler<CompleteProjectTaskCommand>>();
        var completeResult = await complete.HandleAsync(new CompleteProjectTaskCommand(projectId, taskAId, ownerUserId));
        Assert.True(completeResult.IsSuccess);

        var afterCascade = (await getTasks.HandleAsync(new GetProjectTasksByProjectQuery(projectId, ownerUserId))).Value!;
        Assert.Equal(ProjectTaskStatus.Open, afterCascade.Single(t => t.Id == taskBId).Status);

        var getGates = sp.GetRequiredService<IQueryHandler<GetTaskGateConditionsQuery, IReadOnlyList<GateConditionResponse>>>();
        var gates = (await getGates.HandleAsync(new GetTaskGateConditionsQuery(projectId, taskBId, ownerUserId))).Value!;
        Assert.Equal(GateStatus.Satisfied, Assert.Single(gates).Status);
    }

    [Fact]
    public async Task ApprovalGate_RequestsThenDecidesThroughRealOutboxDelivery_SatisfiesGateAndUnlocksTask()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var currentUser = sp.GetRequiredService<TestCurrentUser>();
        var (ownerUserId, _) = await RegisterEmployeeAsync(sp, "approval-owner");
        currentUser.UserId = ownerUserId;
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);

        var (projectId, taskIds) = await CreateProjectWithTasksAsync(sp, ownerUserId, "Needs Approval");
        var taskId = taskIds["Needs Approval"];

        var createGate = sp.GetRequiredService<ICommandHandler<CreateTaskGateConditionCommand, Guid>>();
        var gateResult = await createGate.HandleAsync(new CreateTaskGateConditionCommand(
            projectId, taskId, ownerUserId, ConditionType.Approval, GateTrigger.Unlock, "Manager sign-off",
            ApprovalType: global::Projects.Contracts.ApprovalType.ManagerApproval));
        Assert.True(gateResult.IsSuccess);

        var getTasks = sp.GetRequiredService<IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>>();
        Assert.Equal(ProjectTaskStatus.Locked,
            (await getTasks.HandleAsync(new GetProjectTasksByProjectQuery(projectId, ownerUserId))).Value!
                .Single(t => t.Id == taskId).Status);

        var getGates = sp.GetRequiredService<IQueryHandler<GetTaskGateConditionsQuery, IReadOnlyList<GateConditionResponse>>>();
        var gate = Assert.Single((await getGates.HandleAsync(new GetTaskGateConditionsQuery(projectId, taskId, ownerUserId))).Value!);
        Assert.Equal(GateStatus.Pending, gate.Status);
        Assert.NotNull(gate.ApprovalId);

        // Decide as the project owner - decision #1's approver resolution (owner + stage-team head, no
        // team here) makes the owner the sole eligible decider.
        var decide = sp.GetRequiredService<ICommandHandler<Approvals.Contracts.Approvals.DecideApprovalCommand>>();
        var decideResult = await decide.HandleAsync(new Approvals.Contracts.Approvals.DecideApprovalCommand(
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

        var gateAfter = Assert.Single((await getGates.HandleAsync(new GetTaskGateConditionsQuery(projectId, taskId, ownerUserId))).Value!);
        Assert.Equal(GateStatus.Satisfied, gateAfter.Status);
    }

    [Fact]
    public async Task ApprovalDecidedEvent_ForADesignBriefSubject_DoesNotTouchAnyGate()
    {
        await using var scope = fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var currentUser = sp.GetRequiredService<TestCurrentUser>();
        var (ownerUserId, _) = await RegisterEmployeeAsync(sp, "filter-owner");
        currentUser.UserId = ownerUserId;
        currentUser.Grant(ProjectsPermissions.Projects.ReadAll);

        var (projectId, taskIds) = await CreateProjectWithTasksAsync(sp, ownerUserId, "Untouched Task");
        var taskId = taskIds["Untouched Task"];

        var createGate = sp.GetRequiredService<ICommandHandler<CreateTaskGateConditionCommand, Guid>>();
        var gateId = (await createGate.HandleAsync(new CreateTaskGateConditionCommand(
            projectId, taskId, ownerUserId, ConditionType.ManualCheck, GateTrigger.Unlock, "Manual check"))).Value;

        // Reuses the REAL gate's own id as the fake event's SubjectId - if the SubjectType filter in
        // ApprovalDecidedIntegrationEventHandler didn't work, THIS gate would flip to Satisfied.
        var eventBus = sp.GetRequiredService<IEventBus>();
        await eventBus.PublishAsync(
            new Approvals.Contracts.ApprovalDecidedIntegrationEvent(
                Guid.NewGuid(), Guid.NewGuid(), Approvals.Contracts.ApprovalSubjectType.DesignBrief, gateId,
                Approvals.Contracts.ApprovalDecision.Approved, ownerUserId, DateTimeOffset.UtcNow),
            CancellationToken.None);

        var getGates = sp.GetRequiredService<IQueryHandler<GetTaskGateConditionsQuery, IReadOnlyList<GateConditionResponse>>>();
        var gate = Assert.Single((await getGates.HandleAsync(new GetTaskGateConditionsQuery(projectId, taskId, ownerUserId))).Value!);
        Assert.Equal(GateStatus.Pending, gate.Status);

        var getTasks = sp.GetRequiredService<IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>>();
        Assert.Equal(ProjectTaskStatus.Locked,
            (await getTasks.HandleAsync(new GetProjectTasksByProjectQuery(projectId, ownerUserId))).Value!
                .Single(t => t.Id == taskId).Status);
    }

    private static async Task<(string UserId, Guid EmployeeId)> RegisterEmployeeAsync(IServiceProvider sp, string label)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var register = sp.GetRequiredService<ICommandHandler<RegisterCommand, string>>();
        var registerResult = await register.HandleAsync(new RegisterCommand(
            new RegisterRequest($"{label}-{suffix}@example.com", "P@ssw0rd123!", "Test", label, null, "employee")));
        Assert.True(registerResult.IsSuccess);
        var appUserId = registerResult.Value!;

        var createEmployee = sp.GetRequiredService<ICommandHandler<CreateEmployeeCommand, Guid>>();
        var createResult = await createEmployee.HandleAsync(
            new CreateEmployeeCommand(appUserId, "Engineer", DateOnly.FromDateTime(DateTime.UtcNow), "FullTime"));
        Assert.True(createResult.IsSuccess);

        return (appUserId, createResult.Value);
    }

    /// <summary>One stage template with one <see cref="CreateStageTaskCommand"/> per name in
    /// <paramref name="taskNames"/> (in order), instantiated into a real project. No stage-team/gate
    /// wiring - gate tests build those themselves.</summary>
    private static async Task<(Guid ProjectId, IReadOnlyDictionary<string, Guid> TaskIdsByName)> CreateProjectWithTasksAsync(
        IServiceProvider sp, string ownerUserId, params string[] taskNames)
    {
        var createTemplate = sp.GetRequiredService<ICommandHandler<CreateStageTemplateCommand, Guid>>();
        var templateId = (await createTemplate.HandleAsync(
            new CreateStageTemplateCommand($"Gate-{Guid.NewGuid():N}", StageTemplateType.Custom))).Value;

        var createStage = sp.GetRequiredService<ICommandHandler<CreateStageCommand, Guid>>();
        var stageId = (await createStage.HandleAsync(new CreateStageCommand(templateId, "Only Stage", 1, 100m))).Value;

        var createStageTask = sp.GetRequiredService<ICommandHandler<CreateStageTaskCommand, Guid>>();
        for (var i = 0; i < taskNames.Length; i++)
            Assert.True((await createStageTask.HandleAsync(new CreateStageTaskCommand(stageId, taskNames[i], i + 1))).IsSuccess);

        var createProject = sp.GetRequiredService<ICommandHandler<CreateProjectCommand, Guid>>();
        var projectId = (await createProject.HandleAsync(new CreateProjectCommand(
            $"P-{Guid.NewGuid():N}", "Gate Test Project", "1 Test Rd", global::Projects.Contracts.WorkflowType.Stage,
            Guid.NewGuid(), StageTemplateId: templateId))).Value;

        var getTasks = sp.GetRequiredService<IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>>();
        var tasks = (await getTasks.HandleAsync(new GetProjectTasksByProjectQuery(projectId, ownerUserId))).Value!;

        return (projectId, tasks.ToDictionary(t => t.Name, t => t.Id));
    }
}
