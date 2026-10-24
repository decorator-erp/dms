using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel;

namespace Crm.Infrastructure.Persistence;

/// <summary>
/// Save for every lead and lead-intake write, translating the two races those tables catch into a 409
/// <see cref="CrmLeadErrors.ConcurrentUpdate"/> instead of a 500: an <c>xmin</c> conflict on the lead row
/// (two converts, or two edits, of one lead), and the unique index on <c>crm_lead_intakes.LeadId</c> (two
/// first saves of one lead's intake). Same shape as <see cref="BriefWrites"/>; anything else still throws.
/// </summary>
internal static class LeadWrites
{
    private static readonly HashSet<string> UniqueViolations = new(StringComparer.Ordinal)
    {
        "IX_crm_lead_intakes_LeadId",
    };

    public static async Task<Result> SaveAsync(CrmDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return CrmLeadErrors.ConcurrentUpdate;
        }
        catch (DbUpdateException ex) when (IsMappedUniqueViolation(ex))
        {
            return CrmLeadErrors.ConcurrentUpdate;
        }
    }

    private static bool IsMappedUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: { } name }
        && UniqueViolations.Contains(name);
}
