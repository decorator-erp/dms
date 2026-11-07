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

    /// <summary>P10: in scope, but the caller's effective level on the project is below what the action needs.</summary>
    public static readonly Error InsufficientLevel = Error.Forbidden(
        "projects.InsufficientLevel", "Your access level on this project does not allow this action.");

    /// <summary>G9-13: closes the Phase 2 deferral — <c>Project.ClientId</c> now must name a real
    /// <c>crm.clients</c> row, checked via <c>Crm.Contracts.IClientDirectory.ExistAsync</c> in
    /// <c>CreateProjectCommandHandler</c> before <c>Project.Create</c> is ever called (validation lives
    /// in the handler, never in the aggregate, per the plan's explicit note).</summary>
    public static readonly Error ClientNotFound = Error.NotFound(
        "projects.ClientNotFound", "The specified client was not found.");

    /// <summary>P8: the project code is unique (<c>IX_projects_Code</c>). Pre-checked in
    /// <c>CreateProjectCommandHandler</c>; a create racing past the pre-check trips the index, which
    /// <c>ProjectWrites</c> maps to this same 409.</summary>
    public static readonly Error CodeTaken = Error.Conflict(
        "projects.CodeTaken", "Another project already uses this code.");

    /// <summary>P9: the project row's <c>xmin</c> moved between the read and the save.</summary>
    public static readonly Error ConcurrentUpdate = Error.Conflict(
        "projects.ConcurrentUpdate", "The project was changed by someone else. Reload and try again.");

    /// <summary>NP3: an inactive, on-hold, completed or cancelled project takes no work writes (tasks, sub-tasks,
    /// stages, gates, scope of work, stage teams); its work can still be read.</summary>
    public static readonly Error Closed = Error.Conflict(
        "projects.Closed", "The project is inactive, on hold, completed or cancelled; its work can't change.");

    /// <summary>NP3: a project is marked Completed only when none of its stages is still Active or Locked.</summary>
    public static readonly Error StagesOpen = Error.Conflict(
        "projects.StagesOpen", "Every stage must be completed or rejected before the project can be completed.");

    /// <summary>R1-11: the same rule for a project's stage-less (FreeStyle) work: it is marked Completed only when every
    /// task that belongs to no stage is Completed or Cancelled.</summary>
    public static readonly Error TasksOpen = Error.Conflict(
        "projects.TasksOpen", "Every task must be completed or cancelled before the project can be completed.");

    /// <summary>P1: the project has work under way (a started or touched task, a closed stage, a satisfied or
    /// pending-approval gate), or a document, site (soft-deleted included), CRM or Finance row still names it.</summary>
    public static readonly Error NotDeletable = Error.Conflict(
        "projects.NotDeletable", "This project has started or is referenced elsewhere and cannot be deleted.");
}

internal static class StageErrors
{
    public static readonly Error NotFound = Error.NotFound("stages.NotFound", "Project stage was not found.");

    public static readonly Error InvalidStatusTransition = Error.Conflict(
        "stages.InvalidStatusTransition", "That stage status transition is not allowed.");

    public static readonly Error NegativeCost = Error.BadRequest("stages.NegativeCost", "Cost cannot be negative.");

    /// <summary>NP7 (owner decision): a stage completes only when every one of its tasks is Completed or Cancelled.</summary>
    public static readonly Error TasksOpen = Error.Conflict(
        "stages.TasksOpen", "Every task of this stage must be completed or cancelled before the stage can be completed.");

    /// <summary>A <see cref="GateTrigger.Complete"/> gate on this stage is still <see cref="GateStatus.Pending"/>
    /// — checked by <c>CompleteProjectStageCommandHandler</c> before <c>ProjectStage.Complete</c> runs at
    /// all (G5-08's own facts table: "Complete gates block the owner's completion transition until
    /// satisfied").</summary>
    public static readonly Error GatesNotSatisfied = Error.Conflict(
        "stages.GatesNotSatisfied", "This stage has unsatisfied gate conditions that must be resolved before it can be completed.");

    /// <summary>E2 (D2): a stage is added by hand only once the project's stage structure is settled — the
    /// scope of work is confirmed, or (no scope of work) the project was created from a template.</summary>
    public static readonly Error ScopeNotConfirmed = Error.Conflict(
        "projects.stages.ScopeNotConfirmed", "Stages can be added by hand only after the scope of work is confirmed.");

    /// <summary>E2: a FreeStyle project's tasks may not belong to a stage, so it has no stages to manage.</summary>
    public static readonly Error WorkflowNotSupported = Error.Conflict(
        "projects.stages.WorkflowNotSupported", "A free-style project has no stages.");

    /// <summary>E2: the stage has started (a task left Open/Locked, a done sub-task, a comment or a satisfied
    /// gate), or another gate, a document, CRM or Finance still points at it or its tasks.</summary>
    public static readonly Error NotDeletable = Error.Conflict(
        "projects.stages.NotDeletable", "This stage has started or is referenced elsewhere and cannot be deleted.");
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

    /// <summary>E3 (D2): the task has started (it left Open/Locked), has a done sub-task, a comment, a satisfied
    /// gate or a document, or a gate elsewhere, CRM or Finance points at it.</summary>
    public static readonly Error NotDeletable = Error.Conflict(
        "projects.tasks.NotDeletable", "This task has started or is referenced elsewhere and cannot be deleted.");

    /// <summary>E3: a task is not added to a stage that is already completed or rejected.</summary>
    public static readonly Error StageClosed = Error.Conflict(
        "projects.tasks.StageClosed", "Tasks cannot be added to a completed or rejected stage.");

    /// <summary>R1-10: a task of a Completed stage is not reopened; the completed stage would regain an open task
    /// (NP7 completes a stage only when every task is done).</summary>
    public static readonly Error StageCompleted = Error.Conflict(
        "projects.tasks.StageCompleted", "The task's stage is completed, so the task cannot be reopened.");

    public static readonly Error AssigneeNotFound = Error.NotFound(
        "projects.tasks.AssigneeNotFound", "The assignee was not found or is not active.");

    /// <summary>R1-1: start/complete by someone who is neither the task's assignee, nor a task manager
    /// (<c>stage_tasks.manage</c>), nor a Manager on the project.</summary>
    public static readonly Error NotAssignee = Error.Forbidden(
        "projects.tasks.NotAssignee", "Only the task's assignee, a task manager or a project manager can do this.");

    /// <summary>Same as <see cref="StageErrors.GatesNotSatisfied"/>, for a task-owned
    /// <see cref="GateTrigger.Complete"/> gate.</summary>
    public static readonly Error GatesNotSatisfied = Error.Conflict(
        "tasks.GatesNotSatisfied", "This task has unsatisfied gate conditions that must be resolved before it can be completed.");
}

/// <summary>E1 (D1): project members.</summary>
internal static class ProjectMemberErrors
{
    public static readonly Error NotFound = Error.NotFound("projects.members.NotFound", "Project member was not found.");

    public static readonly Error EmployeeNotFound = Error.NotFound(
        "projects.members.EmployeeNotFound", "The employee was not found or is not active.");

    public static readonly Error AlreadyMember = Error.Conflict(
        "projects.members.AlreadyMember", "This employee is already a member of the project.");

    public static readonly Error ConcurrentUpdate = Error.Conflict(
        "projects.members.ConcurrentUpdate", "The member was changed by someone else. Reload and try again.");
}

internal static class SubTaskErrors
{
    public static readonly Error NotFound = Error.NotFound("subtasks.NotFound", "Project sub-task was not found.");

    public static readonly Error AlreadyDone = Error.Conflict("subtasks.AlreadyDone", "This sub-task is already done.");

    /// <summary>E5: reopen needs a done sub-task.</summary>
    public static readonly Error NotDone = Error.Conflict("projects.subtasks.NotDone", "This sub-task is not done.");

    /// <summary>E5 (D2): a done sub-task has started and stays.</summary>
    public static readonly Error NotDeletable = Error.Conflict(
        "projects.subtasks.NotDeletable", "A done sub-task cannot be deleted.");

    /// <summary>E5: a completed or cancelled task's sub-tasks are frozen.</summary>
    public static readonly Error TaskClosed = Error.Conflict(
        "projects.subtasks.TaskClosed", "The task is completed or cancelled; its sub-tasks can't change.");

    /// <summary>E5: complete/reopen by someone who is neither the task's assignee nor a task manager.</summary>
    public static readonly Error NotAssignee = Error.Forbidden(
        "projects.subtasks.NotAssignee", "Only the task's assignee or a task manager can do this.");
}

/// <summary>E6: task comments.</summary>
internal static class TaskCommentErrors
{
    public static readonly Error NotFound = Error.NotFound("projects.comments.NotFound", "Comment was not found.");

    public static readonly Error NotAuthor = Error.Forbidden(
        "projects.comments.NotAuthor", "Only the comment's author or a task manager can change it.");
}

internal static class TeamErrors
{
    public static readonly Error NotFound = Error.NotFound("teams.NotFound", "Stage team was not found.");

    public static readonly Error MemberNotFound = Error.NotFound("teams.MemberNotFound", "Stage team member was not found.");

    public static readonly Error MemberAlreadyExists = Error.Conflict(
        "teams.MemberAlreadyExists", "This employee is already a member of the stage team.");

    /// <summary>E7: one team per (stage, department).</summary>
    public static readonly Error AlreadyExists = Error.Conflict(
        "projects.teams.AlreadyExists", "This stage already has a team for that department.");

    public static readonly Error HeadNotFound = Error.NotFound(
        "projects.teams.HeadNotFound", "The head was not found or is not active.");

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

    /// <summary>A project stage was made from one of this template's stages (<c>ProjectStage.StageId</c>); the
    /// template stays so that link keeps meaning something.</summary>
    public static readonly Error InUse = Error.Conflict(
        "stage_templates.InUse", "A project was built from this template, so it cannot be deleted.");

    /// <summary>Template names are unique (<c>IX_stage_templates_Name</c>).</summary>
    public static readonly Error NameTaken = Error.Conflict(
        "stage_templates.NameTaken", "Another stage template already uses this name.");
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

    /// <summary>P3: only a Pending <see cref="ConditionType.ManualCheck"/> gate is satisfied by hand. Every other type
    /// is resolved by its own source (an approval decision, a completion, Finance, CRM, Documents), so forcing it
    /// would bypass that source.</summary>
    public static readonly Error NotManuallySatisfiable = Error.Conflict(
        "gate_conditions.NotManuallySatisfiable", "Only a pending manual-check gate can be satisfied by hand.");

    /// <summary>NP2: an Unlock gate locks its owner, so it is added only while the owner is open (a task Open, a stage
    /// Active) or already Locked — never on started, completed, rejected or cancelled work, which locking would
    /// overwrite.</summary>
    public static readonly Error OwnerNotOpen = Error.Conflict(
        "gate_conditions.OwnerNotOpen", "An unlock gate can only be added to work that has not started yet.");

    /// <summary>P4: a satisfied gate is history, and a pending approval gate has an open request in the Approvals
    /// inbox that deleting the gate would orphan.</summary>
    public static readonly Error NotDeletable = Error.Conflict(
        "gate_conditions.NotDeletable", "A satisfied gate, or an approval gate still waiting for its decision, cannot be deleted.");
}

/// <summary>Scope of work (SOW-3). Codes as in the plan's §3.2, plus <see cref="TooManyItems"/>,
/// <see cref="ItemNotFound"/> and <see cref="NegativeFee"/> for rules the plan states without naming.</summary>
internal static class ScopeOfWorkErrors
{
    public static readonly Error Confirmed = Error.Conflict(
        "projects.scope.Confirmed", "The scope of work is confirmed and can no longer be changed.");

    public static readonly Error Empty = Error.BadRequest(
        "projects.scope.Empty", "A scope of work needs at least one item to be confirmed.");

    public static readonly Error DurationRequired = Error.BadRequest(
        "projects.scope.DurationRequired", "Set the duration before confirming the scope of work.");

    public static readonly Error NegativeTotal = Error.BadRequest(
        "projects.scope.NegativeTotal", "The total (fees plus adjustment) cannot be negative.");

    public static readonly Error AdjustmentNoteRequired = Error.BadRequest(
        "projects.scope.AdjustmentNoteRequired", "An adjustment needs a note explaining it.");

    public static readonly Error ProjectHasStages = Error.Conflict(
        "projects.scope.ProjectHasStages", "The project already has stages; a scope of work can't be confirmed into it.");

    public static readonly Error SourceStageNotFound = Error.BadRequest(
        "projects.scope.SourceStageNotFound", "A picked template stage no longer exists.");

    /// <summary>D-3: <see cref="SourceStageNotFound"/>'s code, with the ids that are missing in the detail, so
    /// the caller knows which pick or item to remove.</summary>
    public static Error SourceStagesNotFound(IEnumerable<Guid> ids) => Error.BadRequest(
        SourceStageNotFound.Code, $"These template stages no longer exist: {string.Join(", ", ids)}.");

    /// <summary>D-1 (SoW F1): a FreeStyle project has no stage workflow for a scope of work to create.</summary>
    public static readonly Error WorkflowNotSupported = Error.Conflict(
        "projects.scope.WorkflowNotSupported", "Only a stage-workflow project has a scope of work.");

    public static readonly Error DuplicateItem = Error.Conflict(
        "projects.scope.DuplicateItem", "That template stage is already in the scope of work.");

    public static readonly Error InvalidOrder = Error.BadRequest(
        "projects.scope.InvalidOrder", "The new order must list every item of the scope exactly once.");

    public static readonly Error ConcurrentUpdate = Error.Conflict(
        "projects.scope.ConcurrentUpdate", "The scope of work was changed by someone else. Reload and try again.");

    public static readonly Error TooManyItems = Error.BadRequest(
        "projects.scope.TooManyItems", $"A scope of work holds at most {ScopeOfWork.MaxItems} items.");

    public static readonly Error ItemNotFound = Error.NotFound(
        "projects.scope.ItemNotFound", "That item is not in the scope of work.");

    public static readonly Error NegativeFee = Error.BadRequest(
        "projects.scope.NegativeFee", "A fee cannot be negative.");
}
