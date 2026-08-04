using SharedKernel;

namespace Projects.Domain;

internal static class ProjectErrors
{
    public static readonly Error NotFound = Error.NotFound("projects.NotFound", "Project was not found.");

    public static readonly Error InvalidStatusTransition = Error.Conflict(
        "projects.InvalidStatusTransition", "That project status transition is not allowed.");
}

internal static class StageErrors
{
    public static readonly Error NotFound = Error.NotFound("stages.NotFound", "Project stage was not found.");

    public static readonly Error InvalidStatusTransition = Error.Conflict(
        "stages.InvalidStatusTransition", "That stage status transition is not allowed.");

    public static readonly Error NegativeCost = Error.BadRequest("stages.NegativeCost", "Cost cannot be negative.");
}

internal static class TaskErrors
{
    public static readonly Error NotFound = Error.NotFound("tasks.NotFound", "Project task was not found.");

    public static readonly Error InvalidStatusTransition = Error.Conflict(
        "tasks.InvalidStatusTransition", "That task status transition is not allowed.");

    public static readonly Error StageRequired = Error.BadRequest(
        "tasks.StageRequired", "A stage-workflow project task must belong to a stage.");

    public static readonly Error StageNotAllowed = Error.BadRequest(
        "tasks.StageNotAllowed", "A free-style-workflow project task may not belong to a stage.");

    public static readonly Error SubTasksIncomplete = Error.Conflict(
        "tasks.SubTasksIncomplete", "All required sub-tasks must be done before completing this task.");

    public static readonly Error NegativeCost = Error.BadRequest("tasks.NegativeCost", "Cost cannot be negative.");
}

internal static class SubTaskErrors
{
    public static readonly Error NotFound = Error.NotFound("subtasks.NotFound", "Project sub-task was not found.");

    public static readonly Error AlreadyDone = Error.Conflict("subtasks.AlreadyDone", "This sub-task is already done.");
}

internal static class TeamErrors
{
    public static readonly Error NotFound = Error.NotFound("teams.NotFound", "Stage team was not found.");

    public static readonly Error MemberNotFound = Error.NotFound("teams.MemberNotFound", "Stage team member was not found.");

    public static readonly Error MemberAlreadyExists = Error.Conflict(
        "teams.MemberAlreadyExists", "This employee is already a member of the stage team.");
}
