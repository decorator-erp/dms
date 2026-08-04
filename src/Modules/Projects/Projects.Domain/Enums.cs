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

internal enum WorkflowType
{
    Stage = 1,
    FreeStyle = 2,
}
