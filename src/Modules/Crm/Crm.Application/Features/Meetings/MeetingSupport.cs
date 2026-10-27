using Crm.Contracts.Meetings;
using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Projects.Contracts;
using SharedKernel;

namespace Crm.Application.Features.Meetings;

/// <summary>
/// M1-01: logic shared by more than one Meeting handler. The backend keeps all of this in
/// <c>CrmMeetingDomainService</c>; this module has no domain-service layer, so it lives here instead —
/// same shape <c>BriefMapping</c> uses for the Briefs area.
/// </summary>
internal static class MeetingSupport
{
    /// <summary>
    /// M1-01 backend-bug fix: the ported backend validates an optional period (exists, belongs to this
    /// project, is <c>Open</c>) only on the client-request path — the employee-propose path skipped it
    /// entirely, an asymmetry with no reason behind it. Both create paths call this now.
    /// </summary>
    public static async Task<Result> ValidatePeriodAsync(
        CrmDbContext db, Guid projectId, Guid? periodId, CancellationToken ct)
    {
        if (periodId is not { } pid)
        {
            return Result.Success();
        }

        var period = await db.CrmRequirementPeriods.AsNoTracking()
            .Where(p => p.Id == pid)
            .Select(p => new { p.ProjectId, p.Status })
            .FirstOrDefaultAsync(ct);

        if (period is null || period.ProjectId != projectId)
        {
            return CrmPeriodErrors.NotFound;
        }

        if (period.Status != RequirementPeriodStatus.Open)
        {
            return CrmPeriodErrors.NotOpen;
        }

        return Result.Success();
    }

    public static async Task<CrmMeetingResponse?> ToResponseAsync(CrmDbContext db, Guid meetingId, CancellationToken ct)
    {
        var meeting = await db.CrmMeetings.AsNoTracking()
            .Where(m => m.Id == meetingId)
            .Select(m => new
            {
                m.Id,
                m.ProjectId,
                m.PeriodId,
                m.Title,
                m.Type,
                m.Status,
                m.ScheduledAtUtc,
                m.DurationMinutes,
                m.Location,
                m.RequestedByEmployeeId,
                m.RequestedByContactId,
                m.CanceledAtUtc,
                m.CancelReason,
                m.CreatedAtUtc,
            })
            .FirstOrDefaultAsync(ct);

        if (meeting is null)
        {
            return null;
        }

        List<CrmMeetingSlot> slots = await db.CrmMeetingSlots.AsNoTracking()
            .Where(s => s.MeetingId == meetingId)
            .OrderBy(s => s.StartsAtUtc)
            .ToListAsync(ct);

        return new CrmMeetingResponse(
            meeting.Id, meeting.ProjectId, meeting.PeriodId, meeting.Title,
            CrmEnumMapping.ToContracts(meeting.Type), CrmEnumMapping.ToContracts(meeting.Status),
            meeting.ScheduledAtUtc, meeting.DurationMinutes, meeting.Location,
            meeting.RequestedByEmployeeId, meeting.RequestedByContactId,
            meeting.CanceledAtUtc, meeting.CancelReason, meeting.CreatedAtUtc,
            slots.Select(ToSlotResponse).ToList());
    }

    public static CrmMeetingSlotResponse ToSlotResponse(CrmMeetingSlot s) => new(
        s.Id, s.MeetingId, s.StartsAtUtc, s.EndsAtUtc, CrmEnumMapping.ToContracts(s.Status),
        s.ProposedByEmployeeId, s.ProposedByContactId);

    /// <summary>Employee proposed a slot -> only a contact may accept/decline it; contact proposed it
    /// -> only an employee may. The KIND half of <c>CrmMeetingDomainService.IsCounterpartyOf</c> — see
    /// <see cref="EnsureActorCanActOnProjectAsync"/> for the identity half the backend's version was
    /// missing.</summary>
    public static bool IsCounterpartyKind(CrmMeetingSlot slot, Guid? actingEmployeeId, Guid? actingContactId)
    {
        if (slot.ProposedByEmployeeId is not null)
        {
            return actingContactId is not null && actingEmployeeId is null;
        }

        if (slot.ProposedByContactId is not null)
        {
            return actingEmployeeId is not null && actingContactId is null;
        }

        return false;
    }

    /// <summary>
    /// M1-01 backend-bug fix. The ported backend's <c>IsCounterpartyOf</c> (slots) and
    /// <c>IsRequesterOrCounterparty</c> (meetings) check only the ACTOR KIND — "a contact" or "an
    /// employee" — never a specific identity. Concretely: any client contact, from any company, can
    /// accept an employee-proposed slot or cancel an employee-requested meeting on a project that isn't
    /// theirs; only the caller's own project-membership check (a step outside this method entirely)
    /// happened to catch it in the backend, and this port's <c>ICrmAccessGuard</c> has no equivalent
    /// project-membership check to lean on.
    ///
    /// <para>The fix: an acting EMPLOYEE may act on any meeting — this port's <c>CrmAccessGuard</c>
    /// already grants every employee <c>HasReadAll: true</c>, i.e. company-wide CRM access is this
    /// port's existing security model, not a gap to close here. An acting CONTACT may act only on a
    /// meeting whose project belongs to their own client — the actual tenant boundary a client-facing
    /// operation must hold, verified here via <c>IProjectDirectory</c> rather than assumed.</para>
    /// </summary>
    /// <summary>B-4 (MT-N2): what a CONTACT is told when the meeting or slot is not theirs, or does not exist —
    /// one portal 404, so a client cannot probe another client's meeting or slot ids. Employees keep the
    /// specific office errors (their access is the project scope's, B-3).</summary>
    public static Error NotFoundFor(Guid? actingContactId, Error officeError) =>
        actingContactId is null ? officeError : CrmPortalErrors.NotFound;

    /// <summary>B-4 (MT-N2): the contact's tenant check, run BEFORE the actor-kind check; any failure is the
    /// portal 404. A no-op for an employee actor.</summary>
    public static async Task<Result> EnsureContactOwnsProjectAsync(
        CrmDbContext db, IProjectDirectory projectDirectory, Guid projectId, Guid? actingContactId, CancellationToken ct)
    {
        if (actingContactId is null)
        {
            return Result.Success();
        }

        Result owns = await EnsureActorCanActOnProjectAsync(db, projectDirectory, projectId, null, actingContactId, ct);
        return owns.IsFailure ? CrmPortalErrors.NotFound : Result.Success();
    }

    public static async Task<Result> EnsureActorCanActOnProjectAsync(
        CrmDbContext db, IProjectDirectory projectDirectory,
        Guid projectId, Guid? actingEmployeeId, Guid? actingContactId, CancellationToken ct)
    {
        if (actingEmployeeId is not null)
        {
            return Result.Success();
        }

        if (actingContactId is not { } contactId)
        {
            return CrmMeetingErrors.NotCounterparty;
        }

        Guid? contactClientId = await db.CrmContacts.AsNoTracking()
            .Where(c => c.Id == contactId)
            .Select(c => (Guid?)c.ClientId)
            .FirstOrDefaultAsync(ct);
        if (contactClientId is null)
        {
            return CrmMeetingErrors.NotCounterparty;
        }

        ProjectSummary? project = await projectDirectory.GetProjectAsync(projectId, ct);
        if (project is null || project.ClientId != contactClientId)
        {
            return CrmMeetingErrors.NotCounterparty;
        }

        return Result.Success();
    }
}
