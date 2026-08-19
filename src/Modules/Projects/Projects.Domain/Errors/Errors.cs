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

    /// <summary>G9-13: closes the Phase 2 deferral — <c>Project.ClientId</c> now must name a real
    /// <c>crm.clients</c> row, checked via <c>Crm.Contracts.IClientDirectory.ExistAsync</c> in
    /// <c>CreateProjectCommandHandler</c> before <c>Project.Create</c> is ever called (validation lives
    /// in the handler, never in the aggregate, per the plan's explicit note).</summary>
    public static readonly Error ClientNotFound = Error.NotFound(
        "projects.ClientNotFound", "The specified client was not found.");
}

internal static class StageErrors
{
    public static readonly Error NotFound = Error.NotFound("stages.NotFound", "Project stage was not found.");

    public static readonly Error InvalidStatusTransition = Error.Conflict(
        "stages.InvalidStatusTransition", "That stage status transition is not allowed.");

    public static readonly Error NegativeCost = Error.BadRequest("stages.NegativeCost", "Cost cannot be negative.");

    /// <summary>A <see cref="GateTrigger.Complete"/> gate on this stage is still <see cref="GateStatus.Pending"/>
    /// — checked by <c>CompleteProjectStageCommandHandler</c> before <c>ProjectStage.Complete</c> runs at
    /// all (G5-08's own facts table: "Complete gates block the owner's completion transition until
    /// satisfied").</summary>
    public static readonly Error GatesNotSatisfied = Error.Conflict(
        "stages.GatesNotSatisfied", "This stage has unsatisfied gate conditions that must be resolved before it can be completed.");
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

    /// <summary>Same as <see cref="StageErrors.GatesNotSatisfied"/>, for a task-owned
    /// <see cref="GateTrigger.Complete"/> gate.</summary>
    public static readonly Error GatesNotSatisfied = Error.Conflict(
        "tasks.GatesNotSatisfied", "This task has unsatisfied gate conditions that must be resolved before it can be completed.");
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

    /// <summary>Instantiation-time only (G5-07): a template stage has an active
    /// <c>StageDepartment</c> whose department has no assigned head. Per decision #7, this fails the
    /// WHOLE instantiation — no partial materialization — matching the backend's own hard-block.</summary>
    public static readonly Error MissingHead = Error.Conflict(
        "teams.MissingHead", "A department referenced by this template has no assigned head.");
}

internal static class StageTemplateErrors
{
    public static readonly Error NotFound = Error.NotFound("stage_templates.NotFound", "Stage template was not found.");

    public static readonly Error StagesNotFound = Error.BadRequest(
        "stage_templates.StagesNotFound", "The stage template has no stages defined.");
}

/// <summary>Errors for the TEMPLATE <c>Stage</c>/<c>StageTask</c>/<c>StageSubTask</c> entities — distinct
/// from <see cref="StageErrors"/>, which is the RUNTIME <c>ProjectStage</c>'s own error set. The naming
/// overlap mirrors the backend's own confusing-but-stable permission naming (decision #9) — not fixed
/// here for the same reason.</summary>
internal static class TemplateEntityErrors
{
    public static readonly Error StageNotFound = Error.NotFound("stages.TemplateStageNotFound", "Template stage was not found.");

    public static readonly Error StageTaskNotFound = Error.NotFound("stage_tasks.TemplateNotFound", "Template stage task was not found.");

    public static readonly Error StageSubTaskNotFound = Error.NotFound(
        "stage_sub_tasks.TemplateNotFound", "Template stage sub-task was not found.");
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

    /// <summary>
    /// Raised when no <c>IGateConditionTypeHandler</c> is registered for the requested
    /// <c>ConditionType</c>.
    ///
    /// <para><b>The message no longer enumerates the supported types, and that is the fix.</b> It used
    /// to read "Supported types: Task, Stage, Approval, ManualCheck" — already wrong once Documents and
    /// CRM contributed five more handlers, and wrong for EVERY value once Finance's <c>Budget</c> handler
    /// landed at F1-08 and completed the set. A hand-maintained list inside an error string has no test
    /// that can notice it drifting, so the durable answer is not to keep a list there at all: the
    /// registered handlers are the list, and <c>GateConditionCoverageTests</c> asserts they cover every
    /// value.</para>
    ///
    /// <para>As of F1-08 this error is unreachable through the normal path. It stays because the
    /// registry is a runtime lookup — a module failing to register its handler would reach it — and
    /// because that is precisely what the coverage test is written to catch.</para>
    /// </summary>
    public static readonly Error UnsupportedConditionType = Error.BadRequest(
        "gate_conditions.UnsupportedConditionType",
        "No handler is registered for this gate condition type.");
}
