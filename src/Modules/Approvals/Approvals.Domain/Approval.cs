using SharedKernel;

namespace Approvals.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/Approval.cs</c>, collapsed to fit the sink-module shape:
/// the backend's five nullable subject-FK columns (<c>GateConditionId</c>/<c>DesignBriefId</c>/
/// <c>RequirementItemId</c>/<c>BriefSpaceId</c>/<c>ProjectDocumentId</c>, exactly one non-null, enforced
/// by a DB check constraint) become one <see cref="SubjectId"/> + <see cref="SubjectType"/> pair —
/// Approvals never navigates to the subject (it belongs to another module's schema), so there is nothing
/// for a per-subject-type FK to reference here. <see cref="RequestedByUserId"/> and each
/// <see cref="ApproverEntry.ApproverUserId"/> are Identity <c>ApplicationUser</c> ids (strings), never
/// <c>Employee</c> Guids like the backend's <c>RequestedById</c>/<c>AssignedEmployeeId</c> — the calling
/// module resolves Employee → AppUserId itself before requesting. <see cref="Approvers"/> replaces the
/// backend's single XOR'd assigned approver with a set of eligible deciders (first valid decision wins,
/// no quorum) — a deliberate redesign, not a port; see PROGRESS.md's G4-01 entry for the full reasoning.
/// </summary>
internal sealed class Approval : Entity, IAuditable
{
    private readonly List<ApproverEntry> _approvers = [];

    public ApprovalSubjectType SubjectType { get; private set; }
    public Guid SubjectId { get; private set; }
    public ApprovalType Type { get; private set; }
    public string RequestedByUserId { get; private set; } = string.Empty;
    public IReadOnlyList<ApproverEntry> Approvers => _approvers.AsReadOnly();
    public ApprovalDecision? Decision { get; private set; }
    public string? DecidedByUserId { get; private set; }
    public string? Comment { get; private set; }
    public int Round { get; private set; } = 1;
    public DateTimeOffset? DueAtUtc { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }

    /// <summary>The project the subject belongs to, when the requesting module knows it; null for rows made before
    /// the column existed. A bare id (Approvals never reads another module), denormalised so the inbox can link a
    /// row to its project without resolving the subject.</summary>
    public Guid? ProjectId { get; private set; }

    /// <summary>
    /// Computed, not stored: the backend's <c>ApprovalState</c> is a CRM-artifact confirmation state and
    /// never a property of <c>Approval</c> itself; repurposed here as this entity's own lifecycle.
    /// <c>ChangesRequested</c> maps back to <see cref="ApprovalState.PendingApproval"/> — non-terminal,
    /// matching the backend's own gate-side treatment ("stays Pending... rework loop, not a rejection").
    /// </summary>
    public ApprovalState State => Decision switch
    {
        null => ApprovalState.PendingApproval,
        ApprovalDecision.Approved => ApprovalState.Approved,
        ApprovalDecision.Rejected => ApprovalState.Rejected,
        ApprovalDecision.ChangesRequested => ApprovalState.PendingApproval,
        _ => ApprovalState.PendingApproval,
    };

    private Approval()
    {
        // EF materialization.
    }

    private Approval(Guid id)
        : base(id)
    {
    }

    /// <summary>
    /// Does not check for an already-open approval on the same subject
    /// (<see cref="ApprovalErrors.OpenApprovalExists"/>) — that is a DB existence check across other
    /// rows, which a pure factory method has no way to see; <c>Approvals.Application</c> performs it
    /// before calling this. Does not validate <paramref name="approvers"/> non-empty either — request
    /// shape validation is a command validator's job (FluentValidation), not a domain invariant.
    /// </summary>
    public static Approval Create(
        ApprovalSubjectType subjectType,
        Guid subjectId,
        ApprovalType type,
        string requestedByUserId,
        IReadOnlyCollection<ApproverEntry> approvers,
        DateTimeOffset? dueAtUtc = null,
        Guid? projectId = null) =>
        Create(Guid.CreateVersion7(), subjectType, subjectId, type, requestedByUserId, approvers, dueAtUtc, projectId);

    /// <summary>The caller-supplied-id overload behind <c>ApprovalRequest.ApprovalId</c>: a caller that
    /// must request and then commit its own row in a separate transaction names the approval up front, so
    /// a retry after its own commit failed finds the same approval instead of tripping
    /// <see cref="ApprovalErrors.OpenApprovalExists"/>.</summary>
    public static Approval Create(
        Guid id,
        ApprovalSubjectType subjectType,
        Guid subjectId,
        ApprovalType type,
        string requestedByUserId,
        IReadOnlyCollection<ApproverEntry> approvers,
        DateTimeOffset? dueAtUtc = null,
        Guid? projectId = null)
    {
        var approval = new Approval(id)
        {
            SubjectType = subjectType,
            SubjectId = subjectId,
            Type = type,
            RequestedByUserId = requestedByUserId,
            DueAtUtc = dueAtUtc,
            ProjectId = projectId,
            Round = 1,
        };
        approval._approvers.AddRange(approvers);

        return approval;
    }

    public bool IsEligibleApprover(string userId) => _approvers.Any(a => a.ApproverUserId == userId);

    /// <summary>
    /// <paramref name="isOverride"/> is decided by the caller after checking
    /// <c>ICurrentUser.HasPermission(ApprovalsPermissions.DecideAny)</c> — this entity has no
    /// <c>ICurrentUser</c> access, so it enforces only what it can see on its own: not already decided,
    /// the decider is eligible (unless overridden), and a comment is present for
    /// <see cref="ApprovalDecision.Rejected"/>/<see cref="ApprovalDecision.ChangesRequested"/>. Does not
    /// implement the backend's rework-loop (a fresh <c>Approval</c> row per <c>ChangesRequested</c>,
    /// <c>Round</c> incrementing) — <see cref="Round"/> is ported as an inert field; see PROGRESS.md's
    /// G4-01 entry (decision #4).
    /// </summary>
    public Result Decide(string decidingUserId, ApprovalDecision decision, string? comment, bool isOverride = false)
    {
        if (Decision is not null)
        {
            return ApprovalErrors.AlreadyDecided;
        }

        if (!isOverride && !IsEligibleApprover(decidingUserId))
        {
            return ApprovalErrors.NotAssignedApprover;
        }

        if (decision is ApprovalDecision.Rejected or ApprovalDecision.ChangesRequested
            && string.IsNullOrWhiteSpace(comment))
        {
            return ApprovalErrors.CommentRequired;
        }

        Decision = decision;
        DecidedByUserId = decidingUserId;
        Comment = comment;
        DecidedAtUtc = DateTimeOffset.UtcNow;

        AddDomainEvent(new ApprovalDecidedDomainEvent(Id, SubjectType, SubjectId, decision, decidingUserId));

        return Result.Success();
    }
}
