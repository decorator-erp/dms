namespace Projects.Domain;

/// <summary>Mirrors <c>Projects.Contracts.ProjectStatus</c> — forced duplication, same reasoning as
/// Approvals' G4-01/G4-02 (Domain and Contracts cannot reference each other).</summary>
internal enum ProjectStatus
{
    Draft = 1,
    InProgress = 2,
    OnHold = 3,
    Completed = 4,
    Cancelled = 5,
}

internal enum ProjectTaskStatus
{
    Locked = 1,
    Open = 2,
    InProgress = 3,
    Completed = 4,
    PendingApproval = 5,
    Rejected = 6,
    Cancelled = 7,
}

internal enum StageStatus
{
    Locked = 1,
    Active = 2,
    Completed = 3,
    Rejected = 4,
}

internal enum TaskPriority
{
    Low = 1,
    Medium = 2,
    High = 3,
    Urgent = 4,
}

internal enum StageTeamMemberRole
{
    Responsible = 1,
    Contributor = 2,
    Reviewer = 3,
    Observer = 4,
}

/// <summary>E1 (D1): a project member's access level.</summary>
internal enum ProjectMemberAccess
{
    Viewer = 1,
    Editor = 2,
    Manager = 3,
}

internal enum WorkflowType
{
    Stage = 1,
    FreeStyle = 2,
}

internal enum StageTemplateType
{
    System = 1,
    Custom = 2,
}

/// <summary>Mirrors <c>Projects.Contracts.ConditionType</c> in full — see that file's own remarks on why
/// this can't be shared directly. Only <see cref="Task"/>/<see cref="Stage"/>/<see cref="Approval"/>/
/// <see cref="ManualCheck"/> have a handler this phase; the rest are rejected at gate creation.</summary>
internal enum ConditionType
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

internal enum GateOwnerKind
{
    Task = 1,
    Stage = 2,
}

internal enum GateStatus
{
    Pending = 1,
    Satisfied = 2,
    Rejected = 3,
}

internal enum GateTrigger
{
    Unlock = 1,
    Complete = 2,
}

/// <summary>Mirrors <c>Approvals.Contracts.ApprovalType</c> via <c>Projects.Contracts.ApprovalType</c> —
/// the same forced-duplication chain, one link further down (Domain can't reference Contracts either).</summary>
internal enum ApprovalType
{
    ManagerApproval = 1,
    ClientReview = 2,
}

/// <summary>P1-A: mirrors <c>Projects.Contracts.ProjectAccessArea</c> (EnumMappingParityTests).</summary>
[Flags]
internal enum ProjectAccessArea
{
    None = 0,
    Overview = 1,
    StagesAndTasks = 2,
    Documents = 4,
    Sites = 8,
    Crm = 16,
    Team = 32,
}

/// <summary>P1-A: mirrors <c>Projects.Contracts.ProjectAccessGrantStatus</c> (EnumMappingParityTests).</summary>
internal enum ProjectAccessGrantStatus
{
    Upcoming = 1,
    Active = 2,
    Expired = 3,
    Revoked = 4,
}
