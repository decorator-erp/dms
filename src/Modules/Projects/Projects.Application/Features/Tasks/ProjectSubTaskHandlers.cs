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
/// Recomputes the parent task's <c>CompletedSubTasks</c>/<c>TotalSubTasks</c> counters (required sub-tasks only,
/// P14) through <see cref="ManualSubTasks.RecountAsync"/>, which merges the database with the tracked rows, so the
/// just-completed sub-task counts before its own <c>IsDone</c> flip is saved.
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

        // FINDING-E-3/E2-1: hold the task row (FOR NO KEY UPDATE) against a racing delete and racing ticks.
        ProjectTask? task = await ProjectWriteLock.LockTaskForWriteAsync(db, command.ProjectId, subTask.ProjectTaskId, ct)
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

        await ManualSubTasks.RecountAsync(db, task, ct);

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
