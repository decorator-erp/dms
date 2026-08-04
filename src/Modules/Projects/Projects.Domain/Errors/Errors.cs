using SharedKernel;

namespace Projects.Domain;

internal static class ProjectErrors
{
    public static readonly Error NotFound = Error.NotFound("projects.NotFound", "Project was not found.");

    public static readonly Error InvalidStatusTransition = Error.Conflict(
        "projects.InvalidStatusTransition", "That project status transition is not allowed.");

    /// <summary>Ported from the backend's <c>IProjectAccessService</c> — the permission-based row-level
    /// guard's own denial (G5-05). 404-over-403 per <c>CLAUDE.md</c>'s authorization order (aggregate
    /// state comes last; a caller who can't see the project shouldn't learn it exists) is enforced by
    /// the <c>ProjectScopeDecorator</c>'s own mapping (G5-07), not by this error's own status code.</summary>
    public static readonly Error AccessDenied = Error.Forbidden(
        "projects.AccessDenied", "The current user does not have access to this project.");
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

internal static class StageTemplateErrors
{
    public static readonly Error NotFound = Error.NotFound("stage_templates.NotFound", "Stage template was not found.");

    public static readonly Error StagesNotFound = Error.BadRequest(
        "stage_templates.StagesNotFound", "The stage template has no stages defined.");
}

internal static class GateConditionErrors
{
    public static readonly Error NotFound = Error.NotFound("gate_conditions.NotFound", "Gate condition was not found.");

    public static readonly Error InvalidScope = Error.BadRequest(
        "gate_conditions.InvalidScope", "A gate condition must belong to exactly one of a stage or a task.");

    public static readonly Error InvalidReference = Error.BadRequest(
        "gate_conditions.InvalidReference", "The referenced stage or task is required for this condition type.");

    public static readonly Error SelfReference = Error.BadRequest(
        "gate_conditions.SelfReference", "A gate condition cannot reference its own owner.");

    public static readonly Error ReferenceNotFound = Error.NotFound(
        "gate_conditions.ReferenceNotFound", "The referenced stage or task was not found.");

    public static readonly Error CrossProjectReference = Error.BadRequest(
        "gate_conditions.CrossProjectReference", "A gate condition cannot reference a stage or task in another project.");

    public static readonly Error UnsupportedConditionType = Error.BadRequest(
        "gate_conditions.UnsupportedConditionType",
        "This condition type is not supported yet. Supported types: Task, Stage, Approval, ManualCheck.");
}
