using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel;

namespace Crm.Infrastructure.Persistence;

/// <summary>
/// C-6 (LC-M6, LC-L4): save for every client, child, contact and note write — <see cref="BriefWrites"/>'
/// shape. A stale client row (<c>xmin</c>) is 409 <see cref="ClientErrors.ConcurrentUpdate"/>; a second
/// primary contact on the partial unique index <c>ux_crm_contacts_client_primary</c> is 409
/// <see cref="CrmContactErrors.ConcurrentPrimaryChange"/>. Anything else still throws.
/// </summary>
internal static class ClientWrites
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
            return ClientErrors.ConcurrentUpdate;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_crm_contacts_client_primary",
        })
        {
            return CrmContactErrors.ConcurrentPrimaryChange;
        }
    }
}
