namespace Projects.Infrastructure.Persistence;

/// <summary>
/// P2/NP5 (C1): a project's progress is computed on read, never stored on the project row (so a task
/// transition never moves the project's <c>xmin</c>, P9). It is the share of the project's live tasks that are
/// done: the stages' recounted counters (<see cref="StageCounters"/>) summed, plus the project's unstaged tasks
/// (a FreeStyle project's) counted directly, Cancelled tasks excluded. The two sums are SQL subqueries of the
/// caller's statement; only this final rounding runs in memory.
/// </summary>
internal static class ProjectProgress
{
    public static decimal Percent(int completedTasks, int totalTasks) =>
        totalTasks == 0 ? 0m : Math.Round(100m * completedTasks / totalTasks, 2);
}
