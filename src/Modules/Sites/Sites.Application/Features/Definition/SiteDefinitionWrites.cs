using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Sites.Contracts;
using Sites.Domain;
using Sites.Infrastructure.Persistence;

namespace Sites.Application.Features.Definition;

/// <summary>
/// The shared tail of all 16 definition writes: save, then invalidate the schema cache.
///
/// <para><b>Order is load-bearing.</b> <see cref="ISiteSchemaCache.Invalidate"/> runs only after
/// <c>SaveChangesAsync</c> succeeds — if the write throws, nothing changed and nothing should be
/// dropped. Invalidating first would evict a correct cached schema on every failed write.</para>
///
/// <para><b>Defect B6's fix.</b> Every one of the backend's 16 definition handlers catches only
/// <c>Exception</c> and returns a 500, despite all four definition tables carrying an <c>xmin</c>
/// concurrency token added specifically because "two admins reordering one module's fields in the form
/// builder is exactly the collision this exists for". <c>SiteErrors.ConcurrencyConflict</c> exists and
/// is used consistently by the backend's eleven INSTANCE handlers; the definition layer simply never
/// translates it. Two admins editing one form therefore got an unactionable 500 instead of the 409 that
/// tells them to reload and retry. Translated here, once, for all sixteen.</para>
/// </summary>
internal static class SiteDefinitionWrites
{
    public static async Task<Result> SaveAsync(
        SitesDbContext db, ISiteSchemaCache schemaCache, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SiteErrors.ConcurrencyConflict;
        }
        catch (DbUpdateException)
        {
            return SiteErrors.SaveFailed;
        }

        // Global, never per-module: items are shared across modules, so retiring or reordering one link
        // can change how a DIFFERENT module renders. Per-module invalidation would look correct and be
        // wrong.
        schemaCache.Invalidate();
        return Result.Success();
    }
}
