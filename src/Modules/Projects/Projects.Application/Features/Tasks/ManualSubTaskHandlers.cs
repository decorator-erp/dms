using BuildingBlocks;
using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using Projects.Contracts.Tasks;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Tasks;

internal sealed class CreateProjectSubTaskCommandValidator : AbstractValidator<CreateProjectSubTaskCommand>
{
    public CreateProjectSubTaskCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Order).GreaterThan(0).When(x => x.Order is not null);
    }
}

internal sealed class UpdateProjectSubTaskCommandValidator : AbstractValidator<UpdateProjectSubTaskCommand>
{
    public UpdateProjectSubTaskCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Order).GreaterThan(0);
    }
}

/// <summary>Shared E5 lookups and the counter recount (all sub-tasks count, as
/// <c>CompleteProjectSubTaskCommandHandler</c> counts them).</summary>
internal static class ManualSubTasks
{
    /// <summary>The task, in this project, still open to sub-task changes: 404 if it isn't in the project,
    /// 409 if it is completed or cancelled.</summary>
    public static async Task<Result<Domain.ProjectTask>> OpenTaskAsync(ProjectsDbContext db, Guid projectId, Guid taskId, CancellationToken ct)
    {
        Domain.ProjectTask? task = await db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == taskId && t.ProjectId == projectId, ct);
        if (task is null)
        {
            return Result.Failure<Domain.ProjectTask>(Domain.TaskErrors.NotFound);
        }

        return task.Status is Domain.ProjectTaskStatus.Completed or Domain.ProjectTaskStatus.Cancelled
            ? Result.Failure<Domain.ProjectTask>(Domain.SubTaskErrors.TaskClosed)
            : task;
    }

    /// <summary>Recounts from the tracked rows plus the database, so an added or removed sub-task that is not
    /// saved yet is counted.</summary>
    public static async Task RecountAsync(ProjectsDbContext db, Domain.ProjectTask task, CancellationToken ct)
    {
        List<Domain.ProjectSubTask> rows = await db.ProjectSubTasks.Where(s => s.ProjectTaskId == task.Id).ToListAsync(ct);
        IEnumerable<Domain.ProjectSubTask> live = rows
            .Concat(db.ProjectSubTasks.Local.Where(s => s.ProjectTaskId == task.Id && db.Entry(s).State == EntityState.Added))
            .Where(s => db.Entry(s).State != EntityState.Deleted)
            .DistinctBy(s => s.Id);
        List<Domain.ProjectSubTask> all = [.. live];
        task.RecalculateSubTaskProgress(all.Count(s => s.IsDone), all.Count);
    }
}

internal sealed class CreateProjectSubTaskCommandHandler(ProjectsDbContext db) : ICommandHandler<CreateProjectSubTaskCommand, Guid>
{
    public Task<Result<Guid>> HandleAsync(CreateProjectSubTaskCommand command, CancellationToken ct = default) =>
        ProjectWriteLock.RunAsync(db, command.ProjectId, () => CreateAsync(command, ct), ct);

    private async Task<Result<Guid>> CreateAsync(CreateProjectSubTaskCommand command, CancellationToken ct)
    {
        Result<Domain.ProjectTask> task = await ManualSubTasks.OpenTaskAsync(db, command.ProjectId, command.TaskId, ct);
        if (task.IsFailure)
        {
            return Result.Failure<Guid>(task.Error);
        }

        int order = command.Order ?? (await db.ProjectSubTasks.Where(s => s.ProjectTaskId == command.TaskId)
            .MaxAsync(s => (int?)s.Order, ct) ?? 0) + 1;
        var subTask = Domain.ProjectSubTask.Create(command.TaskId, command.Title.Trim(), order, command.IsRequired, command.Description);
        db.ProjectSubTasks.Add(subTask);
        await ManualSubTasks.RecountAsync(db, task.Value!, ct);
        await db.SaveChangesAsync(ct);
        return subTask.Id;
    }
}

internal sealed class UpdateProjectSubTaskCommandHandler(ProjectsDbContext db) : ICommandHandler<UpdateProjectSubTaskCommand>
{
    public async Task<Result> HandleAsync(UpdateProjectSubTaskCommand command, CancellationToken ct = default)
    {
        Result<Domain.ProjectTask> task = await ManualSubTasks.OpenTaskAsync(db, command.ProjectId, command.TaskId, ct);
        if (task.IsFailure)
        {
            return task.Error;
        }

        Domain.ProjectSubTask? subTask = await db.ProjectSubTasks.FirstOrDefaultAsync(
            s => s.Id == command.SubTaskId && s.ProjectTaskId == command.TaskId, ct);
        if (subTask is null)
        {
            return Domain.SubTaskErrors.NotFound;
        }

        subTask.Update(command.Title.Trim(), command.Description, command.Order, command.IsRequired);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>E5 (D2): only an open (not done) sub-task is deleted; a done one is 409.</summary>
internal sealed class DeleteProjectSubTaskCommandHandler(ProjectsDbContext db) : ICommandHandler<DeleteProjectSubTaskCommand>
{
    public Task<Result> HandleAsync(DeleteProjectSubTaskCommand command, CancellationToken ct = default) =>
        ProjectWriteLock.RunAsync(db, command.ProjectId, () => DeleteAsync(command, ct), ct);

    private async Task<Result> DeleteAsync(DeleteProjectSubTaskCommand command, CancellationToken ct)
    {
        Result<Domain.ProjectTask> task = await ManualSubTasks.OpenTaskAsync(db, command.ProjectId, command.TaskId, ct);
        if (task.IsFailure)
        {
            return task.Error;
        }

        Domain.ProjectSubTask? subTask = await db.ProjectSubTasks.FirstOrDefaultAsync(
            s => s.Id == command.SubTaskId && s.ProjectTaskId == command.TaskId, ct);
        if (subTask is null)
        {
            return Domain.SubTaskErrors.NotFound;
        }

        if (subTask.IsDone)
        {
            return Domain.SubTaskErrors.NotDeletable;
        }

        db.ProjectSubTasks.Remove(subTask);
        await ManualSubTasks.RecountAsync(db, task.Value!, ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

/// <summary>E5: route claim (<c>stage_tasks.manage</c> or <c>stage_tasks.complete</c>, 403) → scope (404) →
/// the caller is the task's assignee or holds <c>stage_tasks.manage</c> (403) → the sub-task is done and the
/// task still open (409).</summary>
internal sealed class ReopenProjectSubTaskCommandHandler(ProjectsDbContext db, IHrDirectory hrDirectory, ICurrentUser currentUser)
    : ICommandHandler<ReopenProjectSubTaskCommand>
{
    public async Task<Result> HandleAsync(ReopenProjectSubTaskCommand command, CancellationToken ct = default)
    {
        Domain.ProjectTask? owning = await db.ProjectTasks.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == command.TaskId && t.ProjectId == command.ProjectId, ct);
        if (owning is null)
        {
            return Domain.TaskErrors.NotFound;
        }

        if (!currentUser.HasPermission(ProjectsPermissions.StageTasks.Manage))
        {
            Guid? me = await hrDirectory.ResolveEmployeeIdAsync(command.UserId, ct);
            if (me is null || owning.AssignedToId != me)
            {
                return Domain.SubTaskErrors.NotAssignee;
            }
        }

        Result<Domain.ProjectTask> task = await ManualSubTasks.OpenTaskAsync(db, command.ProjectId, command.TaskId, ct);
        if (task.IsFailure)
        {
            return task.Error;
        }

        Domain.ProjectSubTask? subTask = await db.ProjectSubTasks.FirstOrDefaultAsync(
            s => s.Id == command.SubTaskId && s.ProjectTaskId == command.TaskId, ct);
        if (subTask is null)
        {
            return Domain.SubTaskErrors.NotFound;
        }

        Result reopened = subTask.Reopen();
        if (reopened.IsFailure)
        {
            return reopened;
        }

        await ManualSubTasks.RecountAsync(db, task.Value!, ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
