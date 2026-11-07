using Crm.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SharedKernel;
using Sites.Domain;
using Sites.Infrastructure.Persistence;

namespace Sites.Application.Features.Surveys;

/// <summary>
/// The shared tails of the instance layer: saving with concurrency translated, and the two
/// cross-aggregate checks that more than one handler needs.
/// </summary>
internal static class SiteWrites
{
    /// <summary>
    /// Unique indexes a race can still hit after a handler's pre-check, by constraint name, mapped to
    /// the same 409 the pre-check answers — <c>CatalogWrites</c>' shape. Anything else stays
    /// <see cref="SiteErrors.SaveFailed"/>.
    /// </summary>
    private static readonly Dictionary<string, Error> UniqueViolations = new(StringComparer.Ordinal)
    {
        ["ux_site_documents_link"] = SiteErrors.DocumentAlreadyLinked,
    };

    /// <summary>
    /// Save, translating an <c>xmin</c> conflict to a 409. Two surveyors editing one survey from two
    /// tablets is the collision the token exists for, and a retry is the whole remedy.
    /// </summary>
    public static async Task<Result> SaveAsync(SitesDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return SiteErrors.ConcurrencyConflict;
        }
        catch (DbUpdateException ex) when (MapUniqueViolation(ex) is { } mapped)
        {
            return mapped;
        }
        catch (DbUpdateException)
        {
            return SiteErrors.SaveFailed;
        }
    }

    private static Error? MapUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: { } name }
        && UniqueViolations.TryGetValue(name, out Error? error)
            ? error
            : null;

    /// <summary>
    /// Loads a site TRACKED, for writing.
    ///
    /// <para><b>The hazard the plan names.</b> The backend's <c>GenericRepository.GetByIdAsync</c>
    /// defaults to <c>asNoTracking: true</c>, and all sixteen of its mutating call sites override it. EF
    /// here tracks by default, so the danger inverts: an <c>AsNoTracking()</c> added for "read
    /// performance" on a write path makes the write vanish with no error and no failing test, unless a
    /// test asserts the PERSISTED value. Every mutating load in this module goes through a helper like
    /// this one, and the tests assert what the database holds afterwards, not just the returned
    /// <c>Result</c>.</para>
    /// </summary>
    public static Task<Site?> LoadForWriteAsync(SitesDbContext db, Guid siteId, CancellationToken ct) =>
        db.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct);

    /// <summary>
    /// The record must belong to the site named in the route.
    ///
    /// <para>Answers <see cref="SiteErrors.RecordNotFound"/> rather than a forbidden, deliberately: a
    /// caller who reached this site is not told that a record they named exists somewhere else. Without
    /// this check the route's site segment is decoration — any caller reaching one site could name any
    /// record in the system.</para>
    /// </summary>
    public static async Task<Result<SiteRecord>> LoadRecordForWriteAsync(
        SitesDbContext db, Guid siteId, Guid recordId, CancellationToken ct)
    {
        SiteRecord? record = await db.SiteRecords
            .FirstOrDefaultAsync(r => r.Id == recordId && r.SiteId == siteId, ct);

        return record is null ? SiteErrors.RecordNotFound : record;
    }

    /// <summary>
    /// The cross-aggregate rule no database constraint can hold: a record's space must belong to the
    /// site's own project.
    ///
    /// <para>The foreign key proves the space row EXISTS; only this proves it is the right project's.
    /// Reads CRM through <c>ICrmSpaceReader</c> — the contract added at S1-01 for this rule alone —
    /// because a module may never query another module's tables. Distinguishes "no such space" from
    /// "retired space" from "wrong project", which the backend cannot: its single existence-and-project
    /// query collapses all three into <c>SpaceNotInProject</c>.</para>
    /// </summary>
    public static async Task<Result> ValidateSpaceAsync(
        ICrmSpaceReader spaces, Guid? spaceId, Guid projectId, CancellationToken ct)
    {
        if (spaceId is not { } id)
        {
            return Result.Success();
        }

        CrmSpaceSummary? space = await spaces.GetAsync(id, ct);

        if (space is null || !space.IsActive)
        {
            return SiteErrors.SpaceNotFound;
        }

        return space.ProjectId == projectId ? Result.Success() : SiteErrors.SpaceNotInProject;
    }
}
