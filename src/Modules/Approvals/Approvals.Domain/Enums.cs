namespace Approvals.Domain;

/// <summary>
/// Mirrors <c>Approvals.Contracts.ApprovalSubjectType</c> — a separate type by necessity, not choice:
/// <c>Approvals.Domain</c> cannot reference <c>Approvals.Contracts</c> (reference matrix: Domain →
/// SharedKernel only) and <c>Approvals.Contracts</c> cannot reference <c>Approvals.Domain</c> either
/// (Contracts → SharedKernel + BuildingBlocks only) — so both sides carry their own copy and
/// <c>Approvals.Application</c> maps between them at the boundary, the same shape HR's
/// <c>ContractType</c>-as-string solved with a string instead of a mirrored enum (see PROGRESS.md's
/// G4-01 entry).
/// </summary>
internal enum ApprovalSubjectType
{
    GateCondition = 1,
    DesignBrief = 2,
    RequirementItem = 3,
    BriefSpace = 4,
    ProjectDocument = 5,
}

/// <summary>Computed on <see cref="Approval"/> from <see cref="Approval.Decision"/> — see
/// <see cref="Approval.State"/> for the mapping and its reasoning.</summary>
internal enum ApprovalState
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    NotRequired = 5,
}

internal enum ApprovalType
{
    ManagerApproval = 1,
    ClientReview = 2,
}

internal enum ApprovalDecision
{
    Approved = 1,
    Rejected = 2,
    ChangesRequested = 3,
}

internal enum ApproverKind
{
    Employee = 1,
    Client = 2,
}
