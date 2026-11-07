using BuildingBlocks;
using Hr.Contracts;
using Projects.Contracts;
using SharedKernel;
using Domain = Projects.Domain;

namespace Projects.Application.Features.Tasks;

/// <summary>
/// R1-1: who may start or complete a task. The route's claim and the Editor level only say the caller works on the
/// project; executing a particular task needs one of: being its assignee, holding <c>stage_tasks.manage</c>, or a
/// Manager level on the project. Anyone else is 403 <see cref="Domain.TaskErrors.NotAssignee"/> — the sub-task
/// rule (<see cref="Domain.SubTaskErrors.NotAssignee"/>) widened by the project level. Comments stay open to any
/// Editor.
/// </summary>
internal static class TaskExecutors
{
    public static async Task<Result> EnsureMayExecuteAsync(
        ICurrentUser currentUser, IProjectAccessLevelReader levels, IHrDirectory hrDirectory,
        Guid projectId, Guid? assignedToId, string userId, CancellationToken ct)
    {
        if (currentUser.HasPermission(ProjectsPermissions.StageTasks.Manage))
        {
            return Result.Success();
        }

        if (assignedToId is { } assignee && await hrDirectory.ResolveEmployeeIdAsync(userId, ct) == assignee)
        {
            return Result.Success();
        }

        return await levels.GetLevelAsync(projectId, userId, ct) == ProjectMemberAccess.Manager
            ? Result.Success()
            : Domain.TaskErrors.NotAssignee;
    }
}
