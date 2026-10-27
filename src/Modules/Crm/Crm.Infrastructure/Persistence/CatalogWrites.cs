using Crm.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel;

namespace Crm.Infrastructure.Persistence;

/// <summary>
/// Save for space and space-type writes — <see cref="BriefWrites"/>' shape (A-7, B-M4). The handlers
/// pre-check each rule for the sequential case; this maps the index violation a race still hits to the
/// same 409 instead of a 500. Anything else still throws.
/// </summary>
internal static class CatalogWrites
{
    private static readonly Dictionary<string, Error> UniqueViolations = new(StringComparer.Ordinal)
    {
        ["ux_crm_spaces_project_name"] = CrmSpaceErrors.DuplicateName,
        ["IX_crm_space_types_Code"] = CrmSpaceTypeErrors.DuplicateCode,
    };

    public static async Task<Result> SaveAsync(CrmDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
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
