using BuildingBlocks.Messaging;
using FluentValidation;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
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
        bool taskExists = await db.ProjectTasks.AsNoTracking()
            .AnyAsync(t => t.Id == command.TaskId && t.ProjectId == command.ProjectId, ct);
        if (!taskExists)
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
        await db.SaveChangesAsync(ct);
        return comment.Id;
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
            .Select(c => new TaskCommentResponse(c.Id, c.TaskId, c.EmployeeId, c.Content, c.CreatedAtUtc))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<TaskCommentResponse>>(comments);
    }
}
