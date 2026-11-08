using BuildingBlocks;
using Projects.Contracts;

namespace Documents.Application;

/// <summary>
/// P1-A: the project-level Documents lists (the project's documents and folders, a folder's documents and children)
/// keep only the rows of the stages a stage-limited project access grant covers. A row's stage is its own, or its
/// task's (one batched <see cref="IProjectDirectory.GetTasksAsync"/> read for the rows that only name a task). A
/// caller reaching the project through membership, <c>read_all</c> or a whole-project grant gets every row, with no
/// extra read beyond the filter's own statements. A stage-less row is hidden from a stage-limited grantee.
/// </summary>
internal static class DocumentStageFilter
{
    public static async Task<List<T>> ApplyAsync<T>(
        IProjectAccessGuard guard, IProjectDirectory projects, ICurrentUser currentUser, Guid projectId, List<T> rows, Func<T, Guid?> stageOf, Func<T, Guid?> taskOf, CancellationToken ct)
    {
        if (rows.Count == 0 || string.IsNullOrEmpty(currentUser.UserId))
        {
            return rows;
        }

        IReadOnlySet<Guid>? visible = await guard.GetStageFilterAsync(projectId, currentUser.UserId, ProjectAccessArea.Documents, ct);
        if (visible is null)
        {
            return rows;
        }

        Guid[] taskIds = [.. rows.Where(r => stageOf(r) is null).Select(taskOf).OfType<Guid>().Distinct()];
        Dictionary<Guid, Guid?> stageOfTask = taskIds.Length == 0
            ? []
            : (await projects.GetTasksAsync(taskIds, ct)).ToDictionary(t => t.Id, t => t.ProjectStageId);

        return [.. rows.Where(r =>
            (stageOf(r) ?? (taskOf(r) is { } task ? stageOfTask.GetValueOrDefault(task) : null)) is { } stage
            && visible.Contains(stage))];
    }
}
