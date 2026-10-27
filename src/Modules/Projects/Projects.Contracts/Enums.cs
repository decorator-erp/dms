namespace Projects.Contracts;

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/ProjectStatus.cs</c>.</summary>
public enum ProjectStatus
{
    Draft = 1,
    InProgress = 2,
    OnHold = 3,
    Completed = 4,
    Cancelled = 5,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/ProjectTaskStatus.cs</c> (the backend file
/// also defines an unrelated <c>AllowedFileCategory</c> enum in the same source file — a Documents-module
/// concern, not ported here).</summary>
public enum ProjectTaskStatus
{
    Locked = 1,
    Open = 2,
    InProgress = 3,
    Completed = 4,
    PendingApproval = 5,
    Rejected = 6,
    Cancelled = 7,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/StageStatus.cs</c>.</summary>
public enum StageStatus
{
    Locked = 1,
    Active = 2,
    Completed = 3,
    Rejected = 4,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/TaskPriority.cs</c>.</summary>
public enum TaskPriority
{
    Low = 1,
    Medium = 2,
    High = 3,
    Urgent = 4,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/StageTeamMemberRole.cs</c>.
/// <c>Responsible</c>/<c>Contributor</c> can execute tasks; <c>Reviewer</c>/<c>Observer</c> cannot.</summary>
public enum StageTeamMemberRole
{
    Responsible = 1,
    Contributor = 2,
    Reviewer = 3,
    Observer = 4,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/WorkflowType.cs</c> — governs whether
/// <c>ProjectTask.ProjectStageId</c> must (<see cref="Stage"/>) or must not (<see cref="FreeStyle"/>) be
/// set.</summary>
public enum WorkflowType
{
    Stage = 1,
    FreeStyle = 2,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/StageTemplateType.cs</c>.</summary>
public enum StageTemplateType
{
    System = 1,
    Custom = 2,
}

/// <summary>
/// Ported in full from <c>../backend/Domain/Enums/ConditionType.cs</c> — only <see cref="Task"/>,
/// <see cref="Stage"/>, <see cref="Approval"/>, <see cref="ManualCheck"/> have a handler in this phase
/// (see the Scope-boundaries table in <c>PLAN-PHASE2.md</c>); the rest are rejected at gate creation,
/// naming the supported set. Lives in <c>Projects.Contracts</c>, not <c>Projects.Domain</c>, because
/// <see cref="IGateConditionTypeHandler"/> — implemented by other modules later (Finance/Documents/CRM) —
/// needs it, and those modules can only ever reference <c>Projects.Contracts</c>.
/// </summary>
public enum ConditionType
{
    Task = 1,
    Stage = 2,
    Approval = 3,
    ManualCheck = 4,
    Attachment = 5,
    Budget = 6,
    CrmPeriodClosed = 7,
    CrmBriefApproved = 8,
    CrmItemsResolved = 9,
    DocumentApproved = 10,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/GateOwnerKind.cs</c>.</summary>
public enum GateOwnerKind
{
    Task = 1,
    Stage = 2,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/GateStatus.cs</c>.</summary>
public enum GateStatus
{
    Pending = 1,
    Satisfied = 2,
    Rejected = 3,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/GateTrigger.cs</c> — <see cref="Unlock"/>
/// gates lock the owner immediately on creation and re-lock it on reset; <see cref="Complete"/> gates
/// block the owner's completion transition until satisfied.</summary>
public enum GateTrigger
{
    Unlock = 1,
    Complete = 2,
}

/// <summary>
/// Mirrors <c>Approvals.Contracts.ApprovalType</c> — a separate type by necessity: <c>Projects.Contracts</c>
/// cannot reference <c>Approvals.Contracts</c> (the reference matrix restricts <c>*.Contracts</c> to
/// <c>SharedKernel</c>/<c>BuildingBlocks</c> only, never another module's <c>*.Contracts</c>). Mapped to
/// <c>Approvals.Contracts.ApprovalType</c> by <c>Projects.Application</c>'s <c>ApprovalHandler</c> (G5-08),
/// the one place that legitimately sees both.
/// </summary>
public enum ApprovalType
{
    ManagerApproval = 1,
    ClientReview = 2,
}

public enum ScopeOfWorkStatus
{
    Draft = 1,
    Confirmed = 2,
}
