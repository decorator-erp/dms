using BuildingBlocks.Messaging;
using BuildingBlocks.Outbox;
using Crm.Contracts;
using Documents.Contracts;
using Finance.Contracts;
using FluentValidation;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Application.Features.Projects;
using Projects.Application.Features.Stages;
using Projects.Contracts;
using Projects.Contracts.Projects;
using Projects.Contracts.Tasks;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Tasks;

internal sealed class CreateProjectTaskCommandValidator : AbstractValidator<CreateProjectTaskCommand>
{
    public CreateProjectTaskCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.Weight).GreaterThan(0).LessThanOrEqualTo(1000);
        RuleFor(x => x.Order).GreaterThan(0).When(x => x.Order is not null);
    }
}

internal sealed class UpdateProjectTaskCommandValidator : AbstractValidator<UpdateProjectTaskCommand>
{
    public UpdateProjectTaskCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Priority).IsInEnum().When(x => x.Priority is not null);
        RuleFor(x => x.Weight).GreaterThan(0).LessThanOrEqualTo(1000).When(x => x.Weight is not null);
        RuleFor(x => x.Order).GreaterThan(0).When(x => x.Order is not null);
    }
}

/// <summary>Shared E3 rules.</summary>
internal static class ManualTasks
{
    /// <summary>A Stage-workflow project needs a settled stage structure (<see cref="ManualStages.EnsureEditableAsync"/>);
    /// a FreeStyle one only needs to be active.</summary>
    public static async Task<Result<Domain.Project>> EnsureEditableAsync(ProjectsDbContext db, Guid projectId, CancellationToken ct)
    {
        // NP3: an inactive, on-hold, completed or cancelled project takes no work writes (409).
        Result live = await ProjectLiveness.EnsureWritableAsync(db, projectId, ct);
        if (live.IsFailure)
        {
            return Result.Failure<Domain.Project>(live.Error);
        }

        Domain.Project project = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == projectId, ct);
        return project.WorkflowType == Domain.WorkflowType.Stage
            ? await ManualStages.EnsureEditableAsync(db, projectId, ct)
            : project;
    }
}

/// <summary>E3 (D2). Route claim → scope (404) → state (409), under the project lock. The assignee must be
/// an active employee (404); an assignment enqueues <see cref="ProjectTaskAssignedIntegrationEvent"/> in the
/// same save, as <c>AssignProjectTaskCommandHandler</c> does. A task starts Open, whatever the stage's lock.</summary>
internal sealed class CreateProjectTaskCommandHandler(ProjectsDbContext db, IHrDirectory hrDirectory)
    : ICommandHandler<CreateProjectTaskCommand, Guid>
{
    public Task<Result<Guid>> HandleAsync(CreateProjectTaskCommand command, CancellationToken ct = default) =>
        ProjectWriteLock.RunAsync(db, command.ProjectId, () => CreateAsync(command, ct), ct);

    private async Task<Result<Guid>> CreateAsync(CreateProjectTaskCommand command, CancellationToken ct)
    {
        Domain.Project? project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == command.ProjectId, ct);
        if (project is null)
        {
            return Result.Failure<Guid>(Domain.ProjectErrors.NotFound);
        }

        // The workflow rule first (400), so a missing stage on a stage project reads as a bad request.
        if (project.WorkflowType == Domain.WorkflowType.Stage && command.ProjectStageId is null)
        {
            return Result.Failure<Guid>(Domain.TaskErrors.StageRequired);
        }

        if (project.WorkflowType == Domain.WorkflowType.FreeStyle && command.ProjectStageId is not null)
        {
            return Result.Failure<Guid>(Domain.TaskErrors.StageNotAllowed);
        }

        Domain.ProjectStage? stage = null;
        if (command.ProjectStageId is { } stageId)
        {
            // R2-1 (NP7): the stage row before its status is read (project → stage → task, R1-2), so a stage completing
            // at the same time — which holds the row while it checks for open tasks — is waited for and seen as
            // Completed (409 StageClosed below) instead of gaining an Open task behind its back.
            stage = await ProjectWriteLock.LockStageForWriteAsync(db, command.ProjectId, stageId, ct)
                ? await db.ProjectStages.AsNoTracking().FirstOrDefaultAsync(s => s.Id == stageId && s.ProjectId == command.ProjectId, ct)
                : null;
            if (stage is null)
            {
                return Result.Failure<Guid>(Domain.StageErrors.NotFound);
            }
        }

        if (command.AssignedToId is { } assigneeId)
        {
            EmployeeSummary? employee = await hrDirectory.GetAsync(assigneeId, ct);
            if (employee is not { IsActive: true })
            {
                return Result.Failure<Guid>(Domain.TaskErrors.AssigneeNotFound);
            }
        }

        // NP3 and the settled-structure rule (409), after the 404s (R1-3).
        Result<Domain.Project> editable = await ManualTasks.EnsureEditableAsync(db, command.ProjectId, ct);
        if (editable.IsFailure)
        {
            return Result.Failure<Guid>(editable.Error);
        }

        if (stage?.Status is Domain.StageStatus.Completed or Domain.StageStatus.Rejected)
        {
            return Result.Failure<Guid>(Domain.TaskErrors.StageClosed);
        }

        int order = command.Order ?? (await db.ProjectTasks
            .Where(t => t.ProjectId == command.ProjectId && t.ProjectStageId == command.ProjectStageId)
            .MaxAsync(t => (int?)t.Order, ct) ?? 0) + 1;

        Result<Domain.ProjectTask> created = Domain.ProjectTask.Create(
            command.ProjectId, command.Name.Trim(), order, project.WorkflowType, command.Weight, command.ProjectStageId,
            priority: ProjectsEnumMapping.ToDomain(command.Priority), description: command.Description,
            dueDate: ProjectDates.ToUtc(command.DueDate));
        if (created.IsFailure)
        {
            return Result.Failure<Guid>(created.Error);
        }

        Domain.ProjectTask task = created.Value!;
        // FINDING-E-2: Open, like a materialised stage's tasks, even when the stage is locked: stage locking is
        // stage-only (a stage's Unlock gate unlocks the stage, never its tasks), so a Locked task here would
        // stay locked for good.
        task.Update(task.Name, task.Description, command.Notes, task.Priority, task.Weight, task.Order, task.DueDate);

        db.ProjectTasks.Add(task);
        if (command.AssignedToId is { } assignee)
        {
            Result assigned = task.Assign(assignee);
            if (assigned.IsFailure)
            {
                return Result.Failure<Guid>(assigned.Error);
            }

            db.Outbox().Enqueue(new ProjectTaskAssignedIntegrationEvent(
                Guid.NewGuid(), task.ProjectId, task.Id, task.Name, assignee, DateTimeOffset.UtcNow));
        }

        await db.SaveChangesAsync(ct);
        await StageCounters.RecountAsync(db, command.ProjectStageId, ct);
        return task.Id;
    }
}

internal sealed class UpdateProjectTaskCommandHandler(ProjectsDbContext db) : ICommandHandler<UpdateProjectTaskCommand>
{
    public Task<Result> HandleAsync(UpdateProjectTaskCommand command, CancellationToken ct = default) =>
        ProjectWriteLock.RunAsync(db, command.ProjectId, () => UpdateAsync(command, ct), ct);

    private async Task<Result> UpdateAsync(UpdateProjectTaskCommand command, CancellationToken ct)
    {
        // R1-3: the task's 404 before the project's 409s.
        if (!await db.ProjectTasks.AsNoTracking().AnyAsync(t => t.Id == command.TaskId && t.ProjectId == command.ProjectId, ct))
        {
            return Domain.TaskErrors.NotFound;
        }

        Result<Domain.Project> editable = await ManualTasks.EnsureEditableAsync(db, command.ProjectId, ct);
        if (editable.IsFailure)
        {
            return editable.Error;
        }

        Domain.ProjectTask? task = await db.ProjectTasks.FirstOrDefaultAsync(
            t => t.Id == command.TaskId && t.ProjectId == command.ProjectId, ct);
        if (task is null)
        {
            return Domain.TaskErrors.NotFound;
        }

        // FINDING-E-5: a field the caller leaves out keeps its value.
        task.Update(command.Name.Trim(), command.Description, command.Notes,
            command.Priority is { } priority ? ProjectsEnumMapping.ToDomain(priority) : task.Priority,
            command.Weight ?? task.Weight, command.Order ?? task.Order, ProjectDates.ToUtc(command.DueDate));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>E3: hard delete only for a never-started task that nothing points at
/// (<see cref="WorkDeletionGuard.CanDeleteTaskAsync"/>); otherwise 409 <c>projects.tasks.NotDeletable</c>.</summary>
internal sealed class DeleteProjectTaskCommandHandler(
    ProjectsDbContext db, IDocumentQueries documents, ICrmProjectWorkUsage crm, IFinanceProjectWorkUsage finance)
    : ICommandHandler<DeleteProjectTaskCommand>
{
    public Task<Result> HandleAsync(DeleteProjectTaskCommand command, CancellationToken ct = default) =>
        ProjectWriteLock.RunAsync(db, command.ProjectId, () => DeleteAsync(command, ct), ct);

    private async Task<Result> DeleteAsync(DeleteProjectTaskCommand command, CancellationToken ct)
    {
        // R1-3: the task's 404 before the project's 409s.
        if (!await db.ProjectTasks.AsNoTracking().AnyAsync(t => t.Id == command.TaskId && t.ProjectId == command.ProjectId, ct))
        {
            return Domain.TaskErrors.NotFound;
        }

        Result<Domain.Project> editable = await ManualTasks.EnsureEditableAsync(db, command.ProjectId, ct);
        if (editable.IsFailure)
        {
            return editable.Error;
        }

        Domain.ProjectTask? task = await db.ProjectTasks.AsNoTracking().FirstOrDefaultAsync(
            t => t.Id == command.TaskId && t.ProjectId == command.ProjectId, ct);
        if (task is null)
        {
            return Domain.TaskErrors.NotFound;
        }

        // R1-2: the stage row (recounted below) before the task row.
        await ProjectWriteLock.LockStageRowAsync(db, task.ProjectStageId, ct);
        var guard = new WorkDeletionGuard(db, documents, crm, finance);
        await guard.LockAsync(null, [task.Id], ct);
        if (!await guard.CanDeleteTaskAsync(task.Id, ct))
        {
            return Domain.TaskErrors.NotDeletable;
        }

        await guard.RemoveTasksAsync([task.Id], ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ProjectWriteLock.IsForeignKeyViolation(ex))
        {
            return Domain.TaskErrors.NotDeletable;
        }

        await StageCounters.RecountAsync(db, task.ProjectStageId, ct);
        return Result.Success();
    }
}

/// <summary>E3: four statements whatever the size — the task, its sub-tasks, its comment count and its
/// gate statuses.</summary>
internal sealed class GetProjectTaskDetailsQueryHandler(ProjectsDbContext db)
    : IQueryHandler<GetProjectTaskDetailsQuery, ProjectTaskDetailsResponse>
{
    public async Task<Result<ProjectTaskDetailsResponse>> HandleAsync(GetProjectTaskDetailsQuery query, CancellationToken ct = default)
    {
        ProjectTaskResponse? task = await db.ProjectTasks.AsNoTracking()
            .Where(t => t.Id == query.TaskId && t.ProjectId == query.ProjectId)
            .Select(GetProjectTaskQueryHandler.ToResponse())
            .FirstOrDefaultAsync(ct);
        if (task is null)
        {
            return Result.Failure<ProjectTaskDetailsResponse>(Domain.TaskErrors.NotFound);
        }

        List<ProjectSubTaskResponse> subTasks = await db.ProjectSubTasks.AsNoTracking()
            .Where(s => s.ProjectTaskId == query.TaskId)
            .OrderBy(s => s.Order)
            .Select(s => new ProjectSubTaskResponse(s.Id, s.ProjectTaskId, s.Title, s.Description, s.Order, s.IsRequired, s.IsDone))
            .ToListAsync(ct);
        int comments = await db.TaskComments.AsNoTracking().CountAsync(c => c.TaskId == query.TaskId, ct);
        List<Domain.GateStatus> gates = await db.GateConditions.AsNoTracking()
            .Where(g => g.ProjectTaskId == query.TaskId)
            .Select(g => g.Status)
            .ToListAsync(ct);

        var summary = new GateSummary(
            gates.Count,
            gates.Count(g => g == Domain.GateStatus.Pending),
            gates.Count(g => g == Domain.GateStatus.Satisfied),
            gates.Count(g => g == Domain.GateStatus.Rejected));
        return new ProjectTaskDetailsResponse(task, subTasks, subTasks.Count, comments, summary);
    }
}
