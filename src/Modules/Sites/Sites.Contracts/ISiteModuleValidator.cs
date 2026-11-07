using SharedKernel;
using Sites.Contracts.Records;

namespace Sites.Contracts;

/// <summary>
/// Validates a proposed value set against a module's current field definitions — unknown item,
/// duplicate item, exactly-one-value-populated, declared-type match, numeric bounds, text length,
/// regex, option membership, and required-but-missing. Ported from the backend's
/// <c>ISiteModuleValidator</c>.
///
/// <para>Returns <b>every</b> failure at once rather than the first, because the caller is a grid: a
/// surveyor fixing one cell per round trip is not a usable capture form.</para>
///
/// <para><b>Reads the definition directly, never through <see cref="ISiteSchemaCache"/>.</b> That
/// asymmetry is the point — the cache serves the RENDERED form and may be up to its TTL stale, while
/// the rule that judges a submission must be current. Routing this through the cache would make a
/// stale schema silently authoritative.</para>
///
/// <para>Module-internal; in Contracts for the reason given on <see cref="ISiteReadQueries"/>.</para>
/// </summary>
public interface ISiteModuleValidator
{
    Task<Result> ValidateAsync(
        Guid moduleId, IReadOnlyList<SiteValueRequest> values, CancellationToken ct = default);

    /// <summary>
    /// Batch, mandatory — never call <see cref="ValidateAsync"/> in a loop over a grid (S7). Judges
    /// every set in one pass over the definition, a fixed number of queries whatever the row count.
    /// Returns one <see cref="Result"/> per set, in the order given, each exactly what
    /// <see cref="ValidateAsync"/> would answer for that set alone.
    /// </summary>
    Task<IReadOnlyList<Result>> ValidateManyAsync(
        IReadOnlyList<SiteModuleValueSet> sets, CancellationToken ct = default);
}

/// <summary>One record's proposed values and the module that judges them — an input to
/// <see cref="ISiteModuleValidator.ValidateManyAsync"/>.</summary>
public sealed record SiteModuleValueSet(Guid ModuleId, IReadOnlyList<SiteValueRequest> Values);
