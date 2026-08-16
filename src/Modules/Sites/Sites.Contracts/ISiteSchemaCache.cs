using Sites.Contracts.Definition;

namespace Sites.Contracts;

/// <summary>
/// Caches one compiled module schema — the form a surveyor's tablet renders — keyed by module code.
/// Ported from the backend's <c>ISiteSchemaCache</c>.
///
/// <para><b>The failure mode this exists to create.</b> A stale schema means surveyors capture against
/// a form that no longer matches the definition the validator judges submissions by, so a perfectly
/// filled form is rejected for a field the tablet never drew. That is why S1-06 tests invalidation on
/// every one of the 16 definition write paths rather than on a representative sample.</para>
///
/// <para><b><see cref="Invalidate"/> is global, never per-module — deliberately.</b> Items are shared
/// across modules, so reordering or retiring one link can change how a DIFFERENT module renders.
/// Per-module invalidation would be correct-looking and wrong.</para>
///
/// <para>The implementation invalidates by bumping a version counter that participates in the cache
/// key, not by evicting entries: every existing key becomes unreachable at once and the orphans fall
/// out on their own TTL. The TTL must stay ABSOLUTE, never sliding — this entry is read on every form
/// open, so a sliding window would never expire, and the TTL is the only bound on staleness across
/// processes (the counter is per-process).</para>
///
/// <para>Module-internal; in Contracts for the reason given on <see cref="ISiteReadQueries"/>.</para>
/// </summary>
public interface ISiteSchemaCache
{
    /// <summary>
    /// Returns the cached schema for <paramref name="moduleCode"/>, building it via
    /// <paramref name="factory"/> on a miss. A null result is never cached — a mistyped module code
    /// must not shadow the real module for the TTL once someone creates it.
    /// </summary>
    Task<SiteModuleSchemaResponse?> GetOrAddAsync(
        string moduleCode,
        Func<CancellationToken, Task<SiteModuleSchemaResponse?>> factory,
        CancellationToken ct = default);

    /// <summary>Invalidates every cached schema. Called after — never before — a definition write's
    /// <c>SaveChangesAsync</c> succeeds: if the write throws, nothing changed and nothing should be
    /// dropped.</summary>
    void Invalidate();
}
