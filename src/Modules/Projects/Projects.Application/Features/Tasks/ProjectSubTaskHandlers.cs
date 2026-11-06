using BuildingBlocks.Messaging;
using Hr.Contracts;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts.Tasks;
using Projects.Domain;
using Projects.Infrastructure.Persistence;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Tasks;

/// <summary>
/// Recomputes the parent task's <c>CompletedSubTasks</c>/<c>TotalSubTasks</c> counters from the database
/// rather than the in-memory <see cref="Domain.ProjectSubTask"/> collection (subtasks aren't loaded as a
/// navigation on <see cref="Domain.ProjectTask"/> — same "bare reference, no navigation" convention as
/// every other cross-aggregate link in this module) — the just-completed row is excluded from the DB
/// count and added back manually since its own <c>IsDone</c> flip hasn't been committed yet.
/// </summary>
internal sealed class CompleteProjectSubTaskCommandHandler(ProjectsDbContext db, IHrDirectory hrDirectory)
    : ICommandHandler<CompleteProjectSubTaskCommand>
{
    public async Task<Result> HandleAsync(CompleteProjectSubTaskCommand command, CancellationToken ct = default)
    {
        ProjectSubTask? subTask = await db.ProjectSubTasks.FindAsync([command.SubTaskId], ct);
        if (subTask is null)
        {
            return Domain.SubTaskErrors.NotFound;
        }

        // FINDING-E-3: hold the task row (FOR SHARE) against a racing task delete.
        ProjectTask? task = await ProjectWriteLock.ShareTaskAsync(db, command.ProjectId, subTask.ProjectTaskId, ct)
            ? await db.ProjectTasks.FirstOrDefaultAsync(t => t.Id == subTask.ProjectTaskId && t.ProjectId == command.ProjectId, ct)
            : null;
        if (task is null)
        {
            return Domain.TaskErrors.NotFound;
        }

        Guid? employeeId = await hrDirectory.ResolveEmployeeIdAsync(command.UserId, ct);
        if (employeeId is null)
        {
            return Domain.ProjectErrors.AccessDenied;
        }

        Result result = subTask.Complete(employeeId.Value);
        if (result.IsFailure)
        {
            return result;
        }

        int totalSubTasks = await db.ProjectSubTasks.CountAsync(st => st.ProjectTaskId == task.Id, ct);
        int completedOthers = await db.ProjectSubTasks.CountAsync(
            st => st.ProjectTaskId == task.Id && st.IsDone && st.Id != subTask.Id, ct);
        task.RecalculateSubTaskProgress(completedOthers + 1, totalSubTasks);

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

internal sealed class GetProjectSubTasksByTaskQueryHandler(ProjectsDbContext db)
    : IQueryHandler<GetProjectSubTasksByTaskQuery, IReadOnlyList<ProjectSubTaskResponse>>
{
    public async Task<Result<IReadOnlyList<ProjectSubTaskResponse>>> HandleAsync(
        GetProjectSubTasksByTaskQuery query, CancellationToken ct = default)
    {
        bool taskExists = await db.ProjectTasks.AsNoTracking()
            .AnyAsync(t => t.Id == query.TaskId && t.ProjectId == query.ProjectId, ct);
        if (!taskExists)
        {
            return Result.Failure<IReadOnlyList<ProjectSubTaskResponse>>(Domain.TaskErrors.NotFound);
        }

        List<ProjectSubTaskResponse> subTasks = await db.ProjectSubTasks.AsNoTracking()
            .Where(st => st.ProjectTaskId == query.TaskId)
            .OrderBy(st => st.Order)
            .Select(st => new ProjectSubTaskResponse(
                st.Id, st.ProjectTaskId, st.Title, st.Description, st.Order, st.IsRequired, st.IsDone))
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<ProjectSubTaskResponse>>(subTasks);
    }
}
