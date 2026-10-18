using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CrmBriefSpace.cs</c> — one gathering entry in the
/// append-only ledger: what was learned about a space, optionally referencing the meeting it
/// summarizes. Confirmed (Approved) entries are locked forever; a correction appends a new entry that
/// supersedes the locked one, which keeps its own approval trail.
/// </summary>
internal sealed class CrmBriefSpace : Entity, IAuditable
{
    public Guid BriefId { get; private set; }

    /// <summary>The real room this entry is written against; null = a general (project-wide) entry.</summary>
    public Guid? SpaceId { get; private set; }

    public int? CountOrPieces { get; private set; }
    public string? Requirements { get; private set; }
    public Guid? MeetingId { get; private set; }
    public ApprovalState ApprovalStatus { get; private set; }

    /// <summary>The approval the entry's current send is bound to; set only while
    /// <see cref="ApprovalState.PendingApproval"/>. Same round-binding rule as
    /// <see cref="CrmDesignBrief.PendingApprovalId"/>.</summary>
    public Guid? PendingApprovalId { get; private set; }

    /// <summary>How many times this entry has been sent — the entry's round number, which the send handler
    /// folds into a deterministic approval id so a retried send finds its own approval.</summary>
    public int SendCount { get; private set; }
    public Guid? SupersedesSpaceId { get; private set; }
    public Guid? ProjectTaskId { get; private set; }
    public Guid CreatedById { get; private set; }

    private CrmBriefSpace() { }

    public static Result<CrmBriefSpace> Create(
        Guid briefId, Guid? spaceId, Guid createdById, int? countOrPieces = null,
        string? requirements = null, Guid? meetingId = null, Guid? projectTaskId = null) => new CrmBriefSpace
        {
            BriefId = briefId,
            SpaceId = spaceId,
            CreatedById = createdById,
            CountOrPieces = countOrPieces,
            Requirements = requirements,
            MeetingId = meetingId,
            ProjectTaskId = projectTaskId,
            ApprovalStatus = ApprovalState.Draft,
        };

    /// <summary>Ledger rule: only a locked (Approved) entry can be superseded.</summary>
    public static Result<CrmBriefSpace> CreateSuperseding(
        CrmBriefSpace original, Guid? spaceId, Guid createdById, int? countOrPieces = null,
        string? requirements = null, Guid? meetingId = null, Guid? projectTaskId = null)
    {
        if (original.ApprovalStatus != ApprovalState.Approved)
        {
            return CrmBriefErrors.OnlyLockedSuperseded;
        }

        Result<CrmBriefSpace> created = Create(original.BriefId, spaceId, createdById, countOrPieces, requirements, meetingId, projectTaskId);
        if (created.IsFailure)
        {
            return created.Error;
        }

        created.Value!.SupersedesSpaceId = original.Id;
        return created;
    }

    public Result Update(Guid? spaceId, int? countOrPieces, string? requirements, Guid? meetingId, Guid? projectTaskId)
    {
        if (ApprovalStatus == ApprovalState.Approved)
        {
            return CrmBriefErrors.EntryLocked;
        }

        if (ApprovalStatus == ApprovalState.PendingApproval)
        {
            return CrmBriefErrors.EntryPending;
        }

        SpaceId = spaceId;
        CountOrPieces = countOrPieces;
        Requirements = requirements;
        MeetingId = meetingId;
        ProjectTaskId = projectTaskId;
        return Result.Success();
    }

    public Result Send(Guid approvalId)
    {
        if (ApprovalStatus is not (ApprovalState.Draft or ApprovalState.Rejected))
        {
            return ApprovalStatus == ApprovalState.Approved ? CrmBriefErrors.EntryLocked : CrmBriefErrors.EntryPending;
        }

        ApprovalStatus = ApprovalState.PendingApproval;
        PendingApprovalId = approvalId;
        SendCount++;
        return Result.Success();
    }

    internal Result MarkApproved(Guid approvalId)
    {
        if (ApprovalStatus != ApprovalState.PendingApproval || PendingApprovalId != approvalId)
        {
            return CrmBriefErrors.NotPending;
        }

        ApprovalStatus = ApprovalState.Approved;
        PendingApprovalId = null;
        return Result.Success();
    }

    internal Result MarkRejected(Guid approvalId)
    {
        if (ApprovalStatus != ApprovalState.PendingApproval || PendingApprovalId != approvalId)
        {
            return CrmBriefErrors.NotPending;
        }

        ApprovalStatus = ApprovalState.Rejected;
        PendingApprovalId = null;
        return Result.Success();
    }
}
