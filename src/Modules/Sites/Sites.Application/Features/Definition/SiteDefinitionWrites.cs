using Microsoft.EntityFrameworkCore;
using Npgsql;
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
    /// <summary>B-S-M3: the case-insensitive code indexes and the one-link-per-(module, item) index — the
    /// races the handlers' pre-checks cannot close — as the same 409s the pre-checks answer.</summary>
    private static readonly Dictionary<string, Error> UniqueViolations = new(StringComparer.Ordinal)
    {
        ["ux_site_modules_code"] = SiteErrors.CodeTaken,
        ["ux_site_items_code"] = SiteErrors.CodeTaken,
        ["ux_site_item_options_item_code"] = SiteErrors.CodeTaken,
        ["ux_site_module_items_module_item"] = SiteErrors.AlreadyLinked,
    };

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
        catch (DbUpdateException ex) when (MapViolation(ex) is { } mapped)
        {
            return mapped;
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

    /// <summary>
    /// B-S-M3: unique violations by index name; foreign-key violations by which side lost. A DELETE
    /// blocked by a RESTRICT reference means the item/module is in use (409); an INSERT or UPDATE whose
    /// parent is gone means the named item/module does not exist (404). The principal is read from the
    /// constraint name — <c>FK_&lt;dependent&gt;_site_items_*</c>, <c>FK_&lt;dependent&gt;_site_modules_*</c>,
    /// and the composite option key, whose option is cascaded from an item delete.
    /// </summary>
    private static Error? MapViolation(DbUpdateException ex)
    {
        if (ex.InnerException is not PostgresException { ConstraintName: { } name } pg)
        {
            return null;
        }

        if (pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return UniqueViolations.TryGetValue(name, out Error? error) ? error : null;
        }

        if (pg.SqlState != PostgresErrorCodes.ForeignKeyViolation)
        {
            return null;
        }

        bool onItem = name.Contains("_site_items_", StringComparison.Ordinal)
                      || name == "fk_site_record_values_option_of_item";
        bool onModule = name.Contains("_site_modules_", StringComparison.Ordinal);
        bool deleting = ex.Entries.Any(e => e.State == EntityState.Deleted);

        return (onItem, onModule, deleting) switch
        {
            (true, _, true) => SiteErrors.ItemInUse,
            (_, true, true) => SiteErrors.ModuleInUse,
            (true, _, false) => SiteErrors.ItemNotFound,
            (_, true, false) => SiteErrors.ModuleNotFound,
            _ => null,
        };
    }
}
