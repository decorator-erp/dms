using Crm.Domain;
using Crm.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel;

namespace Crm.Application.Features.Briefs;

/// <summary>
/// Save for every brief and brief-space write, translating the two races the brief tables now catch into
/// 409s instead of a 500: an <c>xmin</c> conflict on the brief or entry row, and a unique-index violation
/// named in <see cref="UniqueViolations"/>. Same shape as Sites' <c>SiteWrites.SaveAsync</c>; anything
/// else still throws.
/// </summary>
internal static class BriefWrites
{
    private static readonly Dictionary<string, Error> UniqueViolations = new(StringComparer.Ordinal)
    {
        ["IX_crm_brief_head_snapshots_BriefId_Round"] = CrmBriefErrors.ConcurrentSend,
        ["IX_crm_brief_spaces_SupersedesSpaceId"] = CrmBriefErrors.AlreadySuperseded,
        ["IX_crm_design_briefs_ProjectId_ProjectStageId"] = CrmBriefErrors.AlreadyExists,
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
            return CrmBriefErrors.ConcurrencyConflict;
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
