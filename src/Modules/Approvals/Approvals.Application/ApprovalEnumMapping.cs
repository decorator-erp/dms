using Approvals.Contracts;
using Domain = Approvals.Domain;

namespace Approvals.Application;

/// <summary>
/// <c>Approvals.Application</c> legitimately references both <c>Approvals.Contracts</c> and
/// <c>Approvals.Domain</c> (the reference matrix allows Application → own Domain + other modules'
/// Contracts, and this module's own Contracts) — those two projects can't reference each other (see
/// PROGRESS.md's G4-01 entry), so every command/query handler here needs the same mapping. Centralized
/// once rather than duplicated per handler; <c>Approvals.Infrastructure</c>'s <c>ApprovalRequester</c>
/// has its own private copy since it can't see this internal class.
/// </summary>
internal static class ApprovalEnumMapping
{
    public static Domain.ApprovalSubjectType ToDomain(ApprovalSubjectType t) => t switch
    {
        ApprovalSubjectType.GateCondition => Domain.ApprovalSubjectType.GateCondition,
        ApprovalSubjectType.DesignBrief => Domain.ApprovalSubjectType.DesignBrief,
        ApprovalSubjectType.RequirementItem => Domain.ApprovalSubjectType.RequirementItem,
        ApprovalSubjectType.BriefSpace => Domain.ApprovalSubjectType.BriefSpace,
        ApprovalSubjectType.ProjectDocument => Domain.ApprovalSubjectType.ProjectDocument,
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, null),
    };

    public static ApprovalSubjectType ToContracts(Domain.ApprovalSubjectType t) => t switch
    {
        Domain.ApprovalSubjectType.GateCondition => ApprovalSubjectType.GateCondition,
        Domain.ApprovalSubjectType.DesignBrief => ApprovalSubjectType.DesignBrief,
        Domain.ApprovalSubjectType.RequirementItem => ApprovalSubjectType.RequirementItem,
        Domain.ApprovalSubjectType.BriefSpace => ApprovalSubjectType.BriefSpace,
        Domain.ApprovalSubjectType.ProjectDocument => ApprovalSubjectType.ProjectDocument,
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, null),
    };

    public static Domain.ApprovalDecision ToDomain(ApprovalDecision d) => d switch
    {
        ApprovalDecision.Approved => Domain.ApprovalDecision.Approved,
        ApprovalDecision.Rejected => Domain.ApprovalDecision.Rejected,
        ApprovalDecision.ChangesRequested => Domain.ApprovalDecision.ChangesRequested,
        _ => throw new ArgumentOutOfRangeException(nameof(d), d, null),
    };

    public static ApprovalDecision ToContracts(Domain.ApprovalDecision d) => d switch
    {
        Domain.ApprovalDecision.Approved => ApprovalDecision.Approved,
        Domain.ApprovalDecision.Rejected => ApprovalDecision.Rejected,
        Domain.ApprovalDecision.ChangesRequested => ApprovalDecision.ChangesRequested,
        _ => throw new ArgumentOutOfRangeException(nameof(d), d, null),
    };

    public static ApprovalState ToContracts(Domain.ApprovalState s) => s switch
    {
        Domain.ApprovalState.Draft => ApprovalState.Draft,
        Domain.ApprovalState.PendingApproval => ApprovalState.PendingApproval,
        Domain.ApprovalState.Approved => ApprovalState.Approved,
        Domain.ApprovalState.Rejected => ApprovalState.Rejected,
        Domain.ApprovalState.NotRequired => ApprovalState.NotRequired,
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, null),
    };

    public static ApprovalType ToContracts(Domain.ApprovalType t) => t switch
    {
        Domain.ApprovalType.ManagerApproval => ApprovalType.ManagerApproval,
        Domain.ApprovalType.ClientReview => ApprovalType.ClientReview,
        _ => throw new ArgumentOutOfRangeException(nameof(t), t, null),
    };
}
