using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Projects.Contracts;
using Projects.Contracts.Gates;
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

    // ---- helpers ----

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
