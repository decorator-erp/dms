using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using FluentValidation;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Application.Features.Projects;
using Projects.Contracts;
using Projects.Contracts.Tasks;
using Projects.Domain;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Tasks;

internal sealed class AssignProjectTaskCommandValidator : AbstractValidator<AssignProjectTaskCommand>
{
    public AssignProjectTaskCommandValidator() => RuleFor(x => x.EmployeeId).NotEmpty();
}

/// <summary>Validates the assignee via <see cref="IHrDirectory.GetAsync"/> before assigning — an
/// unknown or inactive employee id is rejected, never silently stored (G5-07's own spec).</summary>
internal sealed class AssignProjectTaskCommandHandler(ProjectsDbContext db, IHrDirectory hrDirectory)
    : ICommandHandler<AssignProjectTaskCommand>
{
    public async Task<Result> HandleAsync(AssignProjectTaskCommand command, CancellationToken ct = default)
    {
        // FINDING-E2-3: hold the task row (FOR NO KEY UPDATE) so a racing delete makes this a 404, not a 500.
        ProjectTask? task = await ProjectWriteLock.LockTaskForWriteAsync(db, command.ProjectId, command.TaskId, ct)
            ? await db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == command.TaskId && t.ProjectId == command.ProjectId, ct)
            : null;
        if (task is null)
        {
            return Domain.TaskErrors.NotFound;
        }

        EmployeeSummary? employee = await hrDirectory.GetAsync(command.EmployeeId, ct);
        if (employee is null || !employee.IsActive)
        {
            return Domain.ProjectErrors.AccessDenied;
        }

        // NP3, after the 404s and 403s (R1-3): no work write on an inactive, on-hold, completed or cancelled project.
        Result live = await ProjectLiveness.EnsureWritableAsync(db, command.ProjectId, ct);
        if (live.IsFailure)
        {
            return live;
        }

        Result result = task.Assign(command.EmployeeId);
        if (result.IsFailure)
        {
            return result;
        }

        db.Outbox().Enqueue(new ProjectTaskAssignedIntegrationEvent(
            Guid.NewGuid(), task.ProjectId, task.Id, task.Name, command.EmployeeId, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class StartProjectTaskCommandHandler(
    ProjectsDbContext db, BuildingBlocks.ICurrentUser currentUser, IProjectAccessLevelReader levels, IHrDirectory hrDirectory)
    : ICommandHandler<StartProjectTaskCommand>
{
    public async Task<Result> HandleAsync(StartProjectTaskCommand command, CancellationToken ct = default)
    {
        // FINDING-E-3/E2-1: hold the task row (FOR NO KEY UPDATE) against a racing delete and racing starts;
        // R1-2: its stage row before it (stage before task, everywhere).
        ProjectTask? task = await ProjectWriteLock.LockStageThenTaskForWriteAsync(db, command.ProjectId, command.TaskId, ct)
            ? await db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == command.TaskId && t.ProjectId == command.ProjectId, ct)
            : null;
        if (task is null)
        {
            return Domain.TaskErrors.NotFound;
        }

        // R1-1: the assignee, a task manager or a project Manager (403).
        Result actor = await TaskExecutors.EnsureMayExecuteAsync(
            currentUser, levels, hrDirectory, command.ProjectId, task.ProjectStageId, task.AssignedToId, command.UserId, ct);
        if (actor.IsFailure)
        {
            return actor;
        }

        // NP3, after the 404s and 403s (R1-3): no work write on an inactive, on-hold, completed or cancelled project.
        Result live = await ProjectLiveness.EnsureWritableAsync(db, command.ProjectId, ct);
        if (live.IsFailure)
        {
            return live;
        }

        Result result = task.Start();
        if (result.IsFailure)
        {
            return result;
        }

        db.Outbox().Enqueue(new ProjectTaskStartedIntegrationEvent(
            Guid.NewGuid(), task.ProjectId, task.Id, task.Name, task.AssignedToId, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>Per G5-08's facts table: a <c>GateTrigger.Complete</c> gate blocks the owner's completion
/// transition until satisfied - checked here, before <see cref="Domain.ProjectTask.Complete"/> even
/// runs, so an unsatisfied gate never lets the task-level state machine transition at all.</summary>
internal sealed class CompleteProjectTaskCommandHandler(
    ProjectsDbContext db, BuildingBlocks.ICurrentUser currentUser, IProjectAccessLevelReader levels, IHrDirectory hrDirectory)
    : ICommandHandler<CompleteProjectTaskCommand>
{
    public async Task<Result> HandleAsync(CompleteProjectTaskCommand command, CancellationToken ct = default)
    {
        // R1-2: stage rows before task rows. Own stage (recounted below) and every stage this completion unlocks, then
        // this task and every task it unlocks, each set in id order (GateOwnerLocks).
        Guid? stageId = await ProjectWriteLock.StageOfTaskAsync(db, command.ProjectId, command.TaskId, ct);
        await GateOwnerLocks.LockCompletionCascadeAsync(db, stageId, command.TaskId, null, ct);

        // FINDING-E2-3: hold the task row (FOR NO KEY UPDATE) so a racing delete makes this a 404, not a 500.
        ProjectTask? task = await ProjectWriteLock.LockTaskForWriteAsync(db, command.ProjectId, command.TaskId, ct)
            ? await db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == command.TaskId && t.ProjectId == command.ProjectId, ct)
            : null;
        if (task is null)
        {
            return Domain.TaskErrors.NotFound;
        }

        // R1-1: the assignee, a task manager or a project Manager (403).
        Result actor = await TaskExecutors.EnsureMayExecuteAsync(
            currentUser, levels, hrDirectory, command.ProjectId, task.ProjectStageId, task.AssignedToId, command.UserId, ct);
        if (actor.IsFailure)
        {
            return actor;
        }

        // NP3, after the 404s and 403s (R1-3): no work write on an inactive, on-hold, completed or cancelled project.
        Result live = await ProjectLiveness.EnsureWritableAsync(db, command.ProjectId, ct);
        if (live.IsFailure)
        {
            return live;
        }

        bool hasUnsatisfiedGates = await db.GateConditions.AsNoTracking().AnyAsync(
            g => g.ProjectTaskId == task.Id && g.Trigger == Domain.GateTrigger.Complete && g.Status != Domain.GateStatus.Satisfied, ct);
        if (hasUnsatisfiedGates)
        {
            return Domain.TaskErrors.GatesNotSatisfied;
        }

        // P14: recount the required sub-tasks first, so a row counted under the old all-sub-tasks rule (or before
        // a sub-task's IsRequired changed) is judged by the current rule.
        await ManualSubTasks.RecountAsync(db, task, ct);

        Result result = task.Complete();
        if (result.IsFailure)
        {
            return result;
        }

        db.Outbox().Enqueue(new ProjectTaskCompletedIntegrationEvent(
            Guid.NewGuid(), task.ProjectId, task.Id, task.Name, task.AssignedToId, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);
        await StageCounters.RecountAsync(db, task.ProjectStageId, ct);
        return Result.Success();
    }
}

/// <summary>P7: the reason is free text with a ceiling.</summary>
internal sealed class RejectProjectTaskCommandValidator : AbstractValidator<RejectProjectTaskCommand>
{
    public RejectProjectTaskCommandValidator() => RuleFor(x => x.Reason).MaximumLength(2000);
}

/// <summary>P7: an unknown status filter is a 400.</summary>
internal sealed class GetProjectTasksByProjectQueryValidator : AbstractValidator<GetProjectTasksByProjectQuery>
{
    public GetProjectTasksByProjectQueryValidator()
    {
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status is not null);

        // P13: GetProjectsQueryValidator's caps.
        RuleFor(x => x.PageNumber).InclusiveBetween(1, 1_000_000).When(x => x.PageNumber is not null);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200).When(x => x.PageSize is not null);
    }
}

internal sealed class RejectProjectTaskCommandHandler(ProjectsDbContext db) : ICommandHandler<RejectProjectTaskCommand>
{
    public async Task<Result> HandleAsync(RejectProjectTaskCommand command, CancellationToken ct = default)
    {
        // P11: hold the task row (FOR NO KEY UPDATE) first, as start/complete do, so a racing writer is waited for
        // and seen instead of overwritten. R1-2: its stage row before it (stage before task, everywhere).
        ProjectTask? task = await ProjectWriteLock.LockStageThenTaskForWriteAsync(db, command.ProjectId, command.TaskId, ct)
            ? await db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == command.TaskId && t.ProjectId == command.ProjectId, ct)
            : null;
        if (task is null)
        {
            return Domain.TaskErrors.NotFound;
        }

        // NP3, after the 404s and 403s (R1-3): no work write on an inactive, on-hold, completed or cancelled project.
        Result live = await ProjectLiveness.EnsureWritableAsync(db, command.ProjectId, ct);
        if (live.IsFailure)
        {
            return live;
        }

        Result result = task.Reject(command.Reason);
        if (result.IsFailure)
        {
            return result;
        }

        db.Outbox().Enqueue(new ProjectTaskRejectedIntegrationEvent(
            Guid.NewGuid(), task.ProjectId, task.Id, task.Name, task.AssignedToId, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);
        await StageCounters.RecountAsync(db, task.ProjectStageId, ct);
        return Result.Success();
    }
}

internal sealed class CancelProjectTaskCommandHandler(ProjectsDbContext db) : ICommandHandler<CancelProjectTaskCommand>
{
    public async Task<Result> HandleAsync(CancelProjectTaskCommand command, CancellationToken ct = default)
    {
        // P11: hold the task row (FOR NO KEY UPDATE) first, as start/complete do, so a racing writer is waited for
        // and seen instead of overwritten. R1-2: its stage row before it (stage before task, everywhere).
        ProjectTask? task = await ProjectWriteLock.LockStageThenTaskForWriteAsync(db, command.ProjectId, command.TaskId, ct)
            ? await db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == command.TaskId && t.ProjectId == command.ProjectId, ct)
            : null;
        if (task is null)
        {
            return Domain.TaskErrors.NotFound;
        }

        // NP3, after the 404s and 403s (R1-3): no work write on an inactive, on-hold, completed or cancelled project.
        Result live = await ProjectLiveness.EnsureWritableAsync(db, command.ProjectId, ct);
        if (live.IsFailure)
        {
            return live;
        }

        Result result = task.Cancel();
        if (result.IsFailure)
        {
            return result;
        }

        db.Outbox().Enqueue(new ProjectTaskCancelledIntegrationEvent(
            Guid.NewGuid(), task.ProjectId, task.Id, task.Name, task.AssignedToId, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);
        await StageCounters.RecountAsync(db, task.ProjectStageId, ct);
        return Result.Success();
    }
}

internal sealed class ReopenProjectTaskCommandHandler(ProjectsDbContext db) : ICommandHandler<ReopenProjectTaskCommand>
{
    public async Task<Result> HandleAsync(ReopenProjectTaskCommand command, CancellationToken ct = default)
    {
        // P11: hold the task row (FOR NO KEY UPDATE) first, as start/complete do, so a racing writer is waited for
        // and seen instead of overwritten. R1-2: its stage row before it (stage before task, everywhere).
        ProjectTask? task = await ProjectWriteLock.LockStageThenTaskForWriteAsync(db, command.ProjectId, command.TaskId, ct)
            ? await db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == command.TaskId && t.ProjectId == command.ProjectId, ct)
            : null;
        if (task is null)
        {
            return Domain.TaskErrors.NotFound;
        }

        // NP3, after the 404s and 403s (R1-3): no work write on an inactive, on-hold, completed or cancelled project.
        Result live = await ProjectLiveness.EnsureWritableAsync(db, command.ProjectId, ct);
        if (live.IsFailure)
        {
            return live;
        }

        // R1-10: not behind a Completed stage's back (NP7). The stage row is held (LockStageThenTaskForWriteAsync), so
        // a stage completing at the same time has either committed and is seen here, or waits for this reopen.
        if (task.ProjectStageId is { } stageId
            && await db.ProjectStages.AsNoTracking().AnyAsync(s => s.Id == stageId && s.Status == Domain.StageStatus.Completed, ct))
        {
            return Domain.TaskErrors.StageCompleted;
        }

        Result result = task.Reopen();
        if (result.IsFailure)
        {
            return result;
        }

        db.Outbox().Enqueue(new ProjectTaskReopenedIntegrationEvent(
            Guid.NewGuid(), task.ProjectId, task.Id, task.Name, task.AssignedToId, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);
        await StageCounters.RecountAsync(db, task.ProjectStageId, ct);
        return Result.Success();
    }
}

internal sealed class GetProjectTaskQueryHandler(ProjectsDbContext db) : IQueryHandler<GetProjectTaskQuery, ProjectTaskResponse>
{
    public async Task<Result<ProjectTaskResponse>> HandleAsync(GetProjectTaskQuery query, CancellationToken ct = default)
    {
        ProjectTaskResponse? response = await db.ProjectTasks.AsNoTracking()
            .Where(t => t.Id == query.TaskId && t.ProjectId == query.ProjectId)
            .Select(ToResponse())
            .FirstOrDefaultAsync(ct);

        return response is null ? Result.Failure<ProjectTaskResponse>(Domain.TaskErrors.NotFound) : response;
    }

    internal static System.Linq.Expressions.Expression<Func<Domain.ProjectTask, ProjectTaskResponse>> ToResponse() => t =>
        new ProjectTaskResponse(
            t.Id, t.ProjectId, t.ProjectStageId, t.Name, t.Description, t.Order,
            ProjectsEnumMapping.ToContracts(t.Status), ProjectsEnumMapping.ToContracts(t.Priority), t.Weight,
            t.AssignedToId, t.DepartmentId, t.DueDate, t.Cost, t.Notes);
}

/// <summary>P1-A: a caller whose access is only a stage-limited grant sees only the granted stages' tasks
/// (<see cref="IProjectAccessGuard.GetStageFilterAsync"/>), filtered before paging.</summary>
internal sealed class GetProjectTasksByProjectQueryHandler(ProjectsDbContext db, IProjectAccessGuard guard)
    : IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>
{
    public async Task<Result<IReadOnlyList<ProjectTaskResponse>>> HandleAsync(
        GetProjectTasksByProjectQuery query, CancellationToken ct = default)
    {
        IQueryable<ProjectTask> tasksQuery = db.ProjectTasks.AsNoTracking().Where(t => t.ProjectId == query.ProjectId);
        Guid[]? visible = (await guard.GetStageFilterAsync(
            query.ProjectId, query.UserId, Contracts.ProjectAccessArea.StagesAndTasks, ct))?.ToArray();
        if (visible is not null)
        {
            tasksQuery = tasksQuery.Where(t => t.ProjectStageId != null && visible.Contains(t.ProjectStageId.Value));
        }
        if (query.StageId is { } stageId)
        {
            tasksQuery = tasksQuery.Where(t => t.ProjectStageId == stageId);
        }

        if (query.AssigneeId is { } assigneeId)
        {
            tasksQuery = tasksQuery.Where(t => t.AssignedToId == assigneeId);
        }

        if (query.Status is { } status)
        {
            Domain.ProjectTaskStatus domainStatus = ProjectsEnumMapping.ToDomain(status);
            tasksQuery = tasksQuery.Where(t => t.Status == domainStatus);
        }

        List<ProjectTaskResponse> tasks = await tasksQuery
            .OrderBy(t => t.Order)
            .ThenBy(t => t.Id)
            .PageIfAsked(query.PageNumber, query.PageSize)
            .Select(GetProjectTaskQueryHandler.ToResponse())
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<ProjectTaskResponse>>(tasks);
    }
}
