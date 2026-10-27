using SharedKernel;

namespace Crm.Domain;

/// <summary>
/// Ported from <c>../backend/Domain/Entities/CrmMeetingSlot.cs</c>. A proposed time on a meeting —
/// not an employee's calendar. Proposer is employee XOR contact; the counterparty accepts or
/// declines. At most one <see cref="MeetingSlotStatus.Accepted"/> slot per meeting is a persistence
/// concern (partial unique index, G9-07) — there is no double-booking guard here, a slot is a
/// candidate time, not a hold.
/// </summary>
internal sealed class CrmMeetingSlot : Entity, IAuditable
{
    public Guid MeetingId { get; private set; }
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public MeetingSlotStatus Status { get; private set; }
    public Guid? ProposedByEmployeeId { get; private set; }
    public Guid? ProposedByContactId { get; private set; }

    private CrmMeetingSlot() { }

    public static Result<CrmMeetingSlot> Create(
        Guid meetingId,
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        Guid? proposedByEmployeeId,
        Guid? proposedByContactId,
        DateTime? nowUtc = null)
    {
        if (proposedByEmployeeId.HasValue == proposedByContactId.HasValue)
        {
            return CrmMeetingErrors.InvalidProposer;
        }

        if (endsAtUtc <= startsAtUtc)
        {
            return CrmMeetingErrors.InvalidSlotRange;
        }

        if (nowUtc is { } now && startsAtUtc <= now)
        {
            return CrmMeetingErrors.InvalidSlotRange;
        }

        return new CrmMeetingSlot
        {
            MeetingId = meetingId,
            StartsAtUtc = startsAtUtc,
            EndsAtUtc = endsAtUtc,
            Status = MeetingSlotStatus.Proposed,
            ProposedByEmployeeId = proposedByEmployeeId,
            ProposedByContactId = proposedByContactId,
        };
    }

    /// <summary>B-7 (M3): a slot whose start is at or before <paramref name="nowUtc"/> can no longer be
    /// accepted — the meeting it would schedule is already in the past.</summary>
    public Result Accept(DateTime nowUtc)
    {
        if (Status == MeetingSlotStatus.Proposed && StartsAtUtc <= nowUtc)
        {
            return CrmMeetingErrors.SlotExpired;
        }

        if (Status != MeetingSlotStatus.Proposed)
        {
            return CrmMeetingErrors.SlotNotProposed;
        }

        Status = MeetingSlotStatus.Accepted;
        return Result.Success();
    }

    public Result Decline()
    {
        if (Status != MeetingSlotStatus.Proposed)
        {
            return CrmMeetingErrors.SlotNotProposed;
        }

        Status = MeetingSlotStatus.Declined;
        return Result.Success();
    }

    /// <summary>Reschedule and cancel: retires an accepted or still-proposed slot (B-11).</summary>
    internal void Demote() => Status = MeetingSlotStatus.Declined;
}
