using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using FluentValidation;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
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

internal sealed class StartProjectTaskCommandHandler(ProjectsDbContext db) : ICommandHandler<StartProjectTaskCommand>
{
    public async Task<Result> HandleAsync(StartProjectTaskCommand command, CancellationToken ct = default)
    {
        // FINDING-E-3/E2-1: hold the task row (FOR NO KEY UPDATE) against a racing delete and racing starts.
        ProjectTask? task = await ProjectWriteLock.LockTaskForWriteAsync(db, command.ProjectId, command.TaskId, ct)
            ? await db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == command.TaskId && t.ProjectId == command.ProjectId, ct)
            : null;
        if (task is null)
        {
            return Domain.TaskErrors.NotFound;
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
internal sealed class CompleteProjectTaskCommandHandler(ProjectsDbContext db) : ICommandHandler<CompleteProjectTaskCommand>
{
    public async Task<Result> HandleAsync(CompleteProjectTaskCommand command, CancellationToken ct = default)
    {
        // FINDING-E2-3: hold the task row (FOR NO KEY UPDATE) so a racing delete makes this a 404, not a 500.
        ProjectTask? task = await ProjectWriteLock.LockTaskForWriteAsync(db, command.ProjectId, command.TaskId, ct)
            ? await db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == command.TaskId && t.ProjectId == command.ProjectId, ct)
            : null;
        if (task is null)
        {
            return Domain.TaskErrors.NotFound;
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

        // Own stage (recounted below) and every stage this completion unlocks, in id order (GateOwnerLocks).
        await GateOwnerLocks.LockCompletionCascadeAsync(db, task.ProjectStageId, task.Id, null, ct);

        db.Outbox().Enqueue(new ProjectTaskCompletedIntegrationEvent(
            Guid.NewGuid(), task.ProjectId, task.Id, task.Name, task.AssignedToId, DateTimeOffset.UtcNow));

        await db.SaveChangesAsync(ct);
        await StageCounters.RecountAsync(db, task.ProjectStageId, ct);
        return Result.Success();
    }
}

internal sealed class RejectProjectTaskCommandHandler(ProjectsDbContext db) : ICommandHandler<RejectProjectTaskCommand>
{
    public async Task<Result> HandleAsync(RejectProjectTaskCommand command, CancellationToken ct = default)
    {
        // P11: hold the task row (FOR NO KEY UPDATE) first, as start/complete do, so a racing writer is waited for
        // and seen instead of overwritten.
        ProjectTask? task = await ProjectWriteLock.LockTaskForWriteAsync(db, command.ProjectId, command.TaskId, ct)
            ? await db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == command.TaskId && t.ProjectId == command.ProjectId, ct)
            : null;
        if (task is null)
        {
            return Domain.TaskErrors.NotFound;
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
        // and seen instead of overwritten.
        ProjectTask? task = await ProjectWriteLock.LockTaskForWriteAsync(db, command.ProjectId, command.TaskId, ct)
            ? await db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == command.TaskId && t.ProjectId == command.ProjectId, ct)
            : null;
        if (task is null)
        {
            return Domain.TaskErrors.NotFound;
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
        // and seen instead of overwritten.
        ProjectTask? task = await ProjectWriteLock.LockTaskForWriteAsync(db, command.ProjectId, command.TaskId, ct)
            ? await db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == command.TaskId && t.ProjectId == command.ProjectId, ct)
            : null;
        if (task is null)
        {
            return Domain.TaskErrors.NotFound;
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
            t.AssignedToId, t.DepartmentId, t.DueDate, t.Cost);
}

internal sealed class GetProjectTasksByProjectQueryHandler(ProjectsDbContext db)
    : IQueryHandler<GetProjectTasksByProjectQuery, IReadOnlyList<ProjectTaskResponse>>
{
    public async Task<Result<IReadOnlyList<ProjectTaskResponse>>> HandleAsync(
        GetProjectTasksByProjectQuery query, CancellationToken ct = default)
    {
        IQueryable<ProjectTask> tasksQuery = db.ProjectTasks.AsNoTracking().Where(t => t.ProjectId == query.ProjectId);
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
            .Select(GetProjectTaskQueryHandler.ToResponse())
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<ProjectTaskResponse>>(tasks);
    }
}
