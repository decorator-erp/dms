using BuildingBlocks;
using BuildingBlocks.Auditing;
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

internal sealed class AddTaskCommentCommandValidator : AbstractValidator<AddTaskCommentCommand>
{
    public AddTaskCommentCommandValidator() => RuleFor(x => x.Content).NotEmpty().MaximumLength(4000);
}

internal sealed class AddTaskCommentCommandHandler(ProjectsDbContext db, IHrDirectory hrDirectory)
    : ICommandHandler<AddTaskCommentCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(AddTaskCommentCommand command, CancellationToken ct = default)
    {
        // FINDING-E-3: hold the task row (FOR SHARE) so a racing task delete can't leave this comment orphaned.
        if (!await ProjectWriteLock.ShareTaskAsync(db, command.ProjectId, command.TaskId, ct))
        {
            return Result.Failure<Guid>(Domain.TaskErrors.NotFound);
        }

        Guid? employeeId = await hrDirectory.ResolveEmployeeIdAsync(command.UserId, ct);
        if (employeeId is null)
        {
            return Result.Failure<Guid>(Domain.ProjectErrors.AccessDenied);
        }

        var comment = Domain.TaskComment.Create(command.Content, command.TaskId, employeeId.Value);
        db.TaskComments.Add(comment);

        Domain.ProjectTask task = await db.ProjectTasks.AsNoTracking()
            .FirstAsync(t => t.Id == command.TaskId && t.ProjectId == command.ProjectId, ct);
        ProjectActivities.Task(db, command.UserId, AuditAction.Update, task, $"Commented on task '{task.Name}'");
        await db.SaveChangesAsync(ct);
        return comment.Id;
    }
}

/// <summary>P13: GetProjectsQueryValidator's caps.</summary>
internal sealed class GetTaskCommentsQueryValidator : AbstractValidator<GetTaskCommentsQuery>
{
    public GetTaskCommentsQueryValidator()
    {
        RuleFor(x => x.PageNumber).InclusiveBetween(1, 1_000_000).When(x => x.PageNumber is not null);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200).When(x => x.PageSize is not null);
    }
}

internal sealed class GetTaskCommentsQueryHandler(ProjectsDbContext db)
    : IQueryHandler<GetTaskCommentsQuery, IReadOnlyList<TaskCommentResponse>>
{
    public async Task<Result<IReadOnlyList<TaskCommentResponse>>> HandleAsync(
        GetTaskCommentsQuery query, CancellationToken ct = default)
    {
        bool taskExists = await db.ProjectTasks.AsNoTracking()
            .AnyAsync(t => t.Id == query.TaskId && t.ProjectId == query.ProjectId, ct);
        if (!taskExists)
        {
            return Result.Failure<IReadOnlyList<TaskCommentResponse>>(Domain.TaskErrors.NotFound);
        }

        List<TaskCommentResponse> comments = await db.TaskComments.AsNoTracking()
            .Where(c => c.TaskId == query.TaskId)
            .OrderBy(c => c.CreatedAtUtc)
            .ThenBy(c => c.Id)
            .PageIfAsked(query.PageNumber, query.PageSize)
            .Select(c => new TaskCommentResponse(c.Id, c.TaskId, c.EmployeeId, c.Content, c.CreatedAtUtc, c.EditedAtUtc))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<TaskCommentResponse>>(comments);
    }
}

internal sealed class UpdateTaskCommentCommandValidator : AbstractValidator<UpdateTaskCommentCommand>
{
    public UpdateTaskCommentCommandValidator() => RuleFor(x => x.Content).NotEmpty().MaximumLength(4000);
}

/// <summary>E6: shared author-or-manager check, after the scope decorator (404) and the comment lookup
/// (404, matched on project and task too).</summary>
internal static class TaskCommentAccess
{
    public static async Task<Result<Domain.TaskComment>> FindWritableAsync(
        ProjectsDbContext db, IHrDirectory hrDirectory, ICurrentUser currentUser,
        Guid projectId, Guid taskId, Guid commentId, string userId, CancellationToken ct)
    {
        bool taskInProject = await db.ProjectTasks.AsNoTracking().AnyAsync(t => t.Id == taskId && t.ProjectId == projectId, ct);
        Domain.TaskComment? comment = taskInProject
            ? await db.TaskComments.FirstOrDefaultAsync(c => c.Id == commentId && c.TaskId == taskId, ct)
            : null;
        if (comment is null)
        {
            return Result.Failure<Domain.TaskComment>(Domain.TaskCommentErrors.NotFound);
        }

        if (currentUser.HasPermission(ProjectsPermissions.StageTasks.Manage))
        {
            return comment;
        }

        Guid? me = await hrDirectory.ResolveEmployeeIdAsync(userId, ct);
        return me == comment.EmployeeId ? comment : Result.Failure<Domain.TaskComment>(Domain.TaskCommentErrors.NotAuthor);
    }
}

internal sealed class UpdateTaskCommentCommandHandler(ProjectsDbContext db, IHrDirectory hrDirectory, ICurrentUser currentUser)
    : ICommandHandler<UpdateTaskCommentCommand>
{
    public async Task<Result> HandleAsync(UpdateTaskCommentCommand command, CancellationToken ct = default)
    {
        Result<Domain.TaskComment> comment = await TaskCommentAccess.FindWritableAsync(
            db, hrDirectory, currentUser, command.ProjectId, command.TaskId, command.CommentId, command.UserId, ct);
        if (comment.IsFailure)
        {
            return comment.Error;
        }

        comment.Value!.UpdateContent(command.Content.Trim());
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class DeleteTaskCommentCommandHandler(ProjectsDbContext db, IHrDirectory hrDirectory, ICurrentUser currentUser)
    : ICommandHandler<DeleteTaskCommentCommand>
{
    public async Task<Result> HandleAsync(DeleteTaskCommentCommand command, CancellationToken ct = default)
    {
        Result<Domain.TaskComment> comment = await TaskCommentAccess.FindWritableAsync(
            db, hrDirectory, currentUser, command.ProjectId, command.TaskId, command.CommentId, command.UserId, ct);
        if (comment.IsFailure)
        {
            return comment.Error;
        }

        db.TaskComments.Remove(comment.Value!);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // FINDING-E-7: a racing delete got there first.
            return Domain.TaskCommentErrors.NotFound;
        }

        return Result.Success();
    }
}
