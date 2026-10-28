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

    /// <summary>Ledger rule: only a locked (Approved) entry can be superseded, and the correction stays on
    /// the original's space — a different space is a new entry, not a correction. That an entry is
    /// superseded at most once is a cross-row rule: the handler checks it, and a partial unique index on
    /// <see cref="SupersedesSpaceId"/> backs it.</summary>
    public static Result<CrmBriefSpace> CreateSuperseding(
        CrmBriefSpace original, Guid? spaceId, Guid createdById, int? countOrPieces = null,
        string? requirements = null, Guid? meetingId = null, Guid? projectTaskId = null)
    {
        if (original.ApprovalStatus != ApprovalState.Approved)
        {
            return CrmBriefErrors.OnlyLockedSuperseded;
        }

        if (spaceId != original.SpaceId)
        {
            return CrmBriefErrors.SupersedeSpaceMismatch;
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

        // A correction is pinned to the space of the entry it supersedes (see CreateSuperseding).
        if (SupersedesSpaceId is not null && spaceId != SpaceId)
        {
            return CrmBriefErrors.SupersedeSpaceMismatch;
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

    /// <summary>The entry's counterpart of <see cref="CrmDesignBrief.VoidRound"/>: rebinds the pending send
    /// to <paramref name="nextApprovalId"/> and counts it as a new send.</summary>
    internal Result VoidSend(Guid voidedApprovalId, Guid nextApprovalId)
    {
        if (ApprovalStatus != ApprovalState.PendingApproval || PendingApprovalId != voidedApprovalId)
        {
            return CrmBriefErrors.NotPending;
        }

        PendingApprovalId = nextApprovalId;
        SendCount++;
        return Result.Success();
    }

    /// <summary>D-10 (A-F1): binds a legacy pending send that lost its approval id to
    /// <paramref name="approvalId"/> — the subject's newest approval, or a fresh round's id when it has none
    /// (<paramref name="newSend"/>, counted as a send).</summary>
    internal Result AdoptPendingApproval(Guid approvalId, bool newSend)
    {
        if (ApprovalStatus != ApprovalState.PendingApproval || PendingApprovalId is not null)
        {
            return CrmBriefErrors.NotPending;
        }

        PendingApprovalId = approvalId;
        if (newSend)
        {
            SendCount++;
        }

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
