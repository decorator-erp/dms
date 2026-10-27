using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel;

namespace Crm.Infrastructure.Persistence;

/// <summary>
/// B-8 (M4): save for every meeting write — <see cref="BriefWrites"/>' shape. A meeting carries an
/// <c>xmin</c> token and every accept touches the meeting row, so two racing accepts collide there; the
/// one accepted slot per meeting (<c>ux_crm_meeting_slots_meeting_accepted</c>) is the backstop. Both are
/// a 409 instead of a 500. Anything else still throws.
/// </summary>
internal static class MeetingWrites
{
    public static async Task<Result> SaveAsync(CrmDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return CrmMeetingErrors.ConcurrentUpdate;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_crm_meeting_slots_meeting_accepted",
        })
        {
            return CrmMeetingErrors.ConcurrentUpdate;
        }
    }
}
