using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel;

namespace Crm.Infrastructure.Persistence;

/// <summary>
/// Save for every requirement item and period write — <see cref="BriefWrites"/>' shape for the
/// items/periods area. Turns the races these tables catch into 409s instead of a 500: an <c>xmin</c>
/// conflict (A-9) and a unique-index violation named in <see cref="UniqueViolations"/>. Anything else
/// still throws.
/// </summary>
internal static class ItemWrites
{
    private static readonly Dictionary<string, Error> UniqueViolations = new(StringComparer.Ordinal)
    {
        ["IX_crm_requirement_items_SupersedesItemId"] = CrmItemErrors.AlreadySuperseded,
        ["ux_crm_requirement_periods_open_per_project"] = CrmPeriodErrors.AlreadyOpen,
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
            return CrmItemErrors.ConcurrentUpdate;
        }
        catch (DbUpdateException ex) when (MapUniqueViolation(ex) is { } mapped)
        {
            return mapped;
        }
    }

    private static Error? MapUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: { } name }
        && UniqueViolations.TryGetValue(name, out Error? error)
            ? error
            : null;
}
