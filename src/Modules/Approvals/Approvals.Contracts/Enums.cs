namespace Approvals.Contracts;

/// <summary>
/// Ported in full from <c>../backend/Domain/Enums/ApprovalSubjectType.cs</c> — subjects for modules that
/// don't exist yet (DesignBrief/RequirementItem/BriefSpace are CRM; ProjectDocument is Documents) are
/// just unused values today, not stubs; they are simply never requested until those modules exist.
/// </summary>
public enum ApprovalSubjectType
{
    GateCondition = 1,
    DesignBrief = 2,
    RequirementItem = 3,
    BriefSpace = 4,
    ProjectDocument = 5,
}

/// <summary>
/// In the backend this is a CRM-artifact confirmation state, never a property of <c>Approval</c> itself.
/// Repurposed here as <c>Approval</c>'s own computed lifecycle: <c>Decision is null</c> →
/// <see cref="PendingApproval"/>; <see cref="ApprovalDecision.Approved"/> → <see cref="Approved"/>;
/// <see cref="ApprovalDecision.Rejected"/> → <see cref="Rejected"/>;
/// <see cref="ApprovalDecision.ChangesRequested"/> → <see cref="PendingApproval"/> (non-terminal — the
/// backend's own gate-side handling treats it as a rework loop, not a rejection). <see cref="Draft"/> and
/// <see cref="NotRequired"/> are ported for completeness (the plan's "just values" reasoning for
/// <see cref="ApprovalSubjectType"/>) but this module never produces them.
/// </summary>
public enum ApprovalState
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    NotRequired = 5,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/ApprovalType.cs</c>.</summary>
public enum ApprovalType
{
    ManagerApproval = 1,
    ClientReview = 2,
}

/// <summary>
/// Ported verbatim from <c>../backend/Domain/Enums/ApprovalDecision.cs</c>. No "Pending" value by
/// design — undecided is represented by a null <c>ApprovalSnapshot.Decision</c>.
/// </summary>
public enum ApprovalDecision
{
    Approved = 1,
    Rejected = 2,
    ChangesRequested = 3,
}

/// <summary>Ported verbatim from <c>../backend/Domain/Enums/ApproverKind.cs</c>.</summary>
public enum ApproverKind
{
    Employee = 1,
    Client = 2,
}
